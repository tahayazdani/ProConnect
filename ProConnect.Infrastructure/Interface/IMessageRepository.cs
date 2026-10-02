using ProConnect.Domain.Model;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProConnect.Infrastructure.Interface
{
    public interface IMessageRepository
    {
        /// <summary>
        /// Whether `userid` is actually one of the two participants (userid_a/userid_b) of `matchId` and
        /// that match still exists - called by MessageService before every send/read so one user can't
        /// send or read messages for a match they aren't part of (mirrors MatchRepository.Unmatch's own
        /// ownership check).
        /// </summary>
        Task<bool> IsParticipant(Guid userid, Guid matchId);

        /// <summary>
        /// Appends a new message to `matchId` on behalf of `senderUserId`. Caller (MessageService) is
        /// responsible for the IsParticipant check first - this does not re-check.
        /// </summary>
        Task<Message> SendMessage(Guid matchId, Guid senderUserId, string content);

        /// <summary>
        /// A match's message history, most-recent-first (same ordering convention as
        /// IMatchRepository.GetMatches), DB-level offset/limit so a long-running conversation's full
        /// history is never materialized just to serve one page - see MessageService.GetMessages.
        /// </summary>
        Task<IEnumerable<Message>> GetMessages(Guid matchId, int offset, int limit);

        /// <summary>
        /// Total message count for `matchId`, without loading/ordering/paging any rows - lets
        /// MessageService report total_count/has_more the same way MatchRepository.CountMatches does.
        /// </summary>
        Task<int> CountMessages(Guid matchId);

        /// <summary>
        /// Marks every not-yet-read message in `matchId` that was sent *to* `readerUserId` (i.e. sent by
        /// the other participant) as read, stamping read_at_utc. A bulk, single round-trip update
        /// (ExecuteUpdateAsync) rather than loading rows into memory first - see MessageRepository.
        /// Returns the number of rows actually updated, so MessageService knows whether to push a
        /// MessagesRead notification (re-opening an already-read conversation returns 0, no push needed).
        /// </summary>
        Task<int> MarkAsRead(Guid matchId, Guid readerUserId);
    }
}
