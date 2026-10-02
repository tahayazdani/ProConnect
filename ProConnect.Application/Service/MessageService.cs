using Microsoft.Extensions.Logging;
using ProConnect.Application.Interface;
using ProConnect.Domain.Model;
using ProConnect.Domain.Request;
using ProConnect.Domain.Response;
using ProConnect.Infrastructure.Interface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ProConnect.Application.Service
{
    public class MessageService : IMessageService
    {
        private readonly IMessageRepository _messageRepository;
        private readonly IMessageNotifier _messageNotifier;
        private readonly ILogger<MessageService> _logger;

        public MessageService(IMessageRepository messageRepository, IMessageNotifier messageNotifier, ILogger<MessageService> logger)
        {
            _messageRepository = messageRepository;
            _messageNotifier = messageNotifier;
            _logger = logger;
        }

        public async Task<ProConnectResponse<MessageResponse>> SendMessage(Guid userid, Guid matchId, MessageRequest request)
        {
            try
            {
                if (request is null || string.IsNullOrWhiteSpace(request.content))
                    return new ProConnectResponse<MessageResponse>
                    {
                        issuccessfull = false,
                        errormessage = "content is required.",
                        message = "content is required."
                    };

                // Same ownership check as MatchRepository.Unmatch - stops a user from sending into a
                // conversation for a match that doesn't exist or that they aren't one of the two
                // participants of (also catches a stale/already-unmatched matchId, since Unmatch
                // hard-deletes the matches row).
                var isParticipant = await _messageRepository.IsParticipant(userid, matchId);
                if (!isParticipant)
                    return new ProConnectResponse<MessageResponse>
                    {
                        issuccessfull = false,
                        errormessage = "Match not found.",
                        message = "Match not found."
                    };

                var message = await _messageRepository.SendMessage(matchId, userid, request.content);
                var response = MapToResponse(message);

                // Live push to whichever clients have joined this match's SignalR group - best-effort,
                // see IMessageNotifier's doc comment for why a push failure must not fail this request.
                await TryNotifyNewMessage(response);

                return new ProConnectResponse<MessageResponse>
                {
                    body = response,
                    message = "Message sent."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"SendMessage failed for userid '{userid}' -> matchId '{matchId}'.");
                throw;
            }
        }

        public async Task<ProConnectResponse<MessagePageResponse>> GetMessages(Guid userid, Guid matchId, MessagePageRequest request)
        {
            try
            {
                request ??= new MessagePageRequest();

                var isParticipant = await _messageRepository.IsParticipant(userid, matchId);
                if (!isParticipant)
                    return new ProConnectResponse<MessagePageResponse>
                    {
                        issuccessfull = false,
                        errormessage = "Match not found.",
                        message = "Match not found."
                    };

                // Opening/paging the conversation is what "read" means here - there is no separate mark-
                // as-read endpoint (see IMessageService.GetMessages doc comment). Done before the page
                // fetch below so the very same response already reflects the just-applied read state.
                var readCount = await _messageRepository.MarkAsRead(matchId, userid);
                if (readCount > 0)
                    await TryNotifyMessagesRead(matchId, userid, readCount);

                var offset = (request.page - 1) * request.pagesize;
                var messages = await _messageRepository.GetMessages(matchId, offset, request.pagesize);
                var totalCount = await _messageRepository.CountMessages(matchId);

                var results = (messages ?? Enumerable.Empty<Message>()).Select(MapToResponse).ToList();

                return new ProConnectResponse<MessagePageResponse>
                {
                    body = new MessagePageResponse
                    {
                        results = results,
                        total_count = totalCount,
                        page = request.page,
                        pagesize = request.pagesize,
                        has_more = (long)request.page * request.pagesize < totalCount
                    },
                    message = "Messages have been fetched successfully."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"GetMessages failed for userid '{userid}' -> matchId '{matchId}'.");
                throw;
            }
        }

        // Best-effort live-push helpers - see IMessageNotifier's doc comment for why these swallow
        // (and merely log) rather than rethrow: the underlying write already durably succeeded in
        // Postgres, and losing the push just means the other participant sees the change on their next
        // GET instead of instantly, not that anything is actually lost.
        private async Task TryNotifyNewMessage(MessageResponse message)
        {
            try
            {
                await _messageNotifier.NotifyNewMessage(message);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"NotifyNewMessage failed for matchId '{message.matchid}' (message '{message.messageid}' was still persisted).");
            }
        }

        private async Task TryNotifyMessagesRead(Guid matchId, Guid readerUserId, int readCount)
        {
            try
            {
                await _messageNotifier.NotifyMessagesRead(matchId, readerUserId, readCount);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"NotifyMessagesRead failed for matchId '{matchId}' -> readerUserId '{readerUserId}'.");
            }
        }

        private static MessageResponse MapToResponse(Message message) => new MessageResponse
        {
            messageid = message.messageid,
            matchid = message.matchid,
            senderuserid = message.senderuserid,
            content = message.content,
            sent_at_utc = message.sent_at_utc,
            is_read = message.is_read,
            read_at_utc = message.read_at_utc
        };
    }
}
