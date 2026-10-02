using ProConnect.Domain.Request;
using ProConnect.Domain.Response;
using System;
using System.Threading.Tasks;

namespace ProConnect.Application.Interface
{
    public interface IMessageService
    {
        /// <summary>
        /// Sends a plain-text message to the other side of `matchId` on behalf of `userid`. Fails (same
        /// "soft error" convention as MatchService.Unmatch/BlockUser - issuccessfull = false,
        /// errormessage set, default 200 statuscode) if `matchId` doesn't exist or `userid` isn't one of
        /// its two participants, so one user can never send into a conversation they aren't part of.
        /// </summary>
        Task<ProConnectResponse<MessageResponse>> SendMessage(Guid userid, Guid matchId, MessageRequest request);

        /// <summary>
        /// Paged message history for `matchId`, most-recent-first (see IMessageRepository.GetMessages).
        /// Same participant check as SendMessage. As a side effect, marks every message in this
        /// conversation sent *to* `userid` as read (see IMessageRepository.MarkAsRead) - there is no
        /// separate "mark as read" endpoint; opening/paging the conversation is what read means here.
        /// Never cached - a conversation should always reflect the latest message/read state immediately.
        /// </summary>
        Task<ProConnectResponse<MessagePageResponse>> GetMessages(Guid userid, Guid matchId, MessagePageRequest request);
    }
}
