using Doppler.Push.Api.Contract;
using Flurl;
using Flurl.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Doppler.Push.Api.Services.FirebaseSentMessagesHandling
{
    public class FirebaseSentMessagesHandler : IFirebaseSentMessagesHandler
    {
        private readonly FirebaseSentMessagesHandlerSettings _settings;
        private readonly IPushContactApiTokenGetter _pushContactApiTokenGetter;
        private readonly ILogger<FirebaseSentMessagesHandler> _logger;

        public FirebaseSentMessagesHandler(
            IOptions<FirebaseSentMessagesHandlerSettings> settings,
            IPushContactApiTokenGetter pushContactApiTokenGetter,
            ILogger<FirebaseSentMessagesHandler> logger)
        {
            _settings = settings.Value;
            _pushContactApiTokenGetter = pushContactApiTokenGetter;
            _logger = logger;
        }

        public async Task HandleSentMessagesAsync(FirebaseMessageSendResponse firebaseMessageSendResponse)
        {
            if (firebaseMessageSendResponse == null)
            {
                throw new ArgumentNullException(nameof(firebaseMessageSendResponse));
            }

            if (firebaseMessageSendResponse.Responses == null || !firebaseMessageSendResponse.Responses.Any())
            {
                return;
            }

            var sentMessagesWithNotValidDeviceToken = firebaseMessageSendResponse.Responses
            .Where(x => !x.IsSuccess && _settings.FatalMessagingErrorCodes.Any(y => y == x.Exception.MessagingErrorCode));

            if (!sentMessagesWithNotValidDeviceToken.Any())
            {
                var notHandlingSentMessages = firebaseMessageSendResponse.Responses.Where(x => !sentMessagesWithNotValidDeviceToken.Any(y => y.MessageId == x.MessageId));

                _logger.LogWarning("Not handling for following Firebase sent messages: {@notHandlingSentMessages}", notHandlingSentMessages);

                return;
            }

            try
            {
                var notValidDeviceTokens = sentMessagesWithNotValidDeviceToken.Select(x => x.DeviceToken);

                var pushContactApiToken = await _pushContactApiTokenGetter.GetTokenAsync();

                var response = await _settings.PushContactApiUrl
                    .AppendPathSegment("PushContact")
                    .WithHeader("Authorization", $"Bearer {pushContactApiToken}")
                    .SendJsonAsync(HttpMethod.Delete, notValidDeviceTokens);

                if (response.StatusCode != 200)
                {
                    _logger.LogError(@"Error deleting push contacts with following
device tokens: {@notValidDeviceTokens}.
Response status code: {@StatusCode}, ", notValidDeviceTokens, response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling following sent messages: {@sentMessages}", sentMessagesWithNotValidDeviceToken);

                //TODO queue messages to try again
            }

            //TODO handle sent messages with MessagingErrorCode not in _settings.FatalMessagingErrorCodes
        }
    }
}
