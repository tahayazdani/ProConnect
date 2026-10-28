using Microsoft.Extensions.Logging;
using Moq;
using ProConnect.Application.Interface;
using ProConnect.Application.Service;
using ProConnect.Domain.Config;
using ProConnect.Domain.Const;
using ProConnect.Domain.Model;
using ProConnect.Domain.Request;
using ProConnect.Domain.Response;
using ProConnect.Infrastructure.Interface;
using ProConnect.Infrastructure.Repository;

namespace ProConnect.Tests.Application.Service
{
    /// <summary>
    /// MatchService's every public method ends up touching the concrete Cache (criteria updates/block
    /// invalidate the recommendation cache; GetRecommendations/WarmRecommendations read/grow it directly -
    /// see FeatureGateServiceTests/SearchServiceTests for the same situation), so this always constructs a
    /// real Cache wrapping a mocked ICache rather than passing null. By default the ICache mock reports a
    /// cache miss for every key (forcing GrowBatch/ComputeEffectivePlan-equivalent logic to run against the
    /// mocked repositories each time); SetupCachedRecommendationBatch below opts a specific test into a
    /// cache hit instead, to cover the "already warm, don't touch the DB" branches. Covers the main
    /// success/failure branch of every public method; not every private scoring helper (ScoreCandidate/
    /// MapToCandidate) gets its own test, since those are exercised indirectly through the public methods
    /// that call them.
    /// </summary>
    public class MatchServiceTests
    {
        private readonly Mock<IMatchCriteriaRepository> _matchCriteriaRepository = new();
        private readonly Mock<IMatchRepository> _matchRepository = new();
        private readonly Mock<IUserRepository> _userRepository = new();
        private readonly Mock<IFeatureGateService> _featureGateService = new();
        private readonly Mock<INotificationService> _notificationService = new();
        private readonly Mock<ICache> _icache = new();

        public MatchServiceTests()
        {
            _icache.Setup(c => c.GetCacheItem<CacheItem<MatchRecommendationBatch>>(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(default(CacheItem<CacheItem<MatchRecommendationBatch>>));
            _icache.Setup(c => c.Set(It.IsAny<string>(), It.IsAny<CacheItem<MatchRecommendationBatch>>(), It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<ExpirationMode>()))
                .Returns(Task.CompletedTask);
            _icache.Setup(c => c.Exists(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
            _icache.Setup(c => c.Delete(It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);
        }

        private void SetupCachedRecommendationBatch(Guid userid, MatchRecommendationBatch batch)
        {
            _icache.Setup(c => c.GetCacheItem<CacheItem<MatchRecommendationBatch>>($"recommendations:{userid}", CacheRegion.MATCHRECOMMENDATION))
                .ReturnsAsync(new CacheItem<CacheItem<MatchRecommendationBatch>>
                {
                    Exists = true,
                    Value = new CacheItem<MatchRecommendationBatch> { Value = batch, ExpiresOn = DateTime.UtcNow.AddMinutes(10) }
                });
        }

        private MatchService CreateService()
        {
            var cache = new Cache(new Mock<ILogger<Cache>>().Object, _icache.Object, new CacheAsideConfig { enablecacheaside = true });
            var cacheExpiryConfig = new CacheExpiryConfig { matchrecommendation = 900 };

            return new MatchService(
                _matchCriteriaRepository.Object,
                _matchRepository.Object,
                _userRepository.Object,
                _featureGateService.Object,
                _notificationService.Object,
                cache,
                cacheExpiryConfig,
                new Mock<ILogger<MatchService>>().Object);
        }

        // --- GetCriteria ---

        [Fact]
        public async Task GetCriteria_WithNoStoredOverrides_FallsBackToProfileFields()
        {
            var userid = Guid.NewGuid();
            _matchCriteriaRepository.Setup(r => r.GetByUserId(userid)).ReturnsAsync((MatchCriteria)null!);
            _userRepository.Setup(r => r.GetById(userid)).ReturnsAsync(new UserRequest { userid = userid, city = "Austin", country = "USA" });

            var service = CreateService();

            var response = await service.GetCriteria(userid);

            Assert.True(response.issuccessfull);
            Assert.Equal("Austin", response.body!.city);
            Assert.Equal("USA", response.body!.country);
        }

        [Fact]
        public async Task GetCriteria_WithStoredOverride_TakesPrecedenceOverProfile()
        {
            var userid = Guid.NewGuid();
            _matchCriteriaRepository.Setup(r => r.GetByUserId(userid))
                .ReturnsAsync(new MatchCriteria { userid = userid, city = "Denver" });
            _userRepository.Setup(r => r.GetById(userid)).ReturnsAsync(new UserRequest { userid = userid, city = "Austin" });

            var service = CreateService();

            var response = await service.GetCriteria(userid);

            Assert.Equal("Denver", response.body!.city);
        }

        // --- UpdateCriteria ---

        [Fact]
        public async Task UpdateCriteria_WithNullRequest_ReturnsFailure()
        {
            var service = CreateService();

            var response = await service.UpdateCriteria(Guid.NewGuid(), null!);

            Assert.False(response.issuccessfull);
            Assert.Equal("Request body is required.", response.errormessage);
        }

        [Fact]
        public async Task UpdateCriteria_WithMinAgeGreaterThanMaxAge_ReturnsFailure()
        {
            var service = CreateService();

            var response = await service.UpdateCriteria(Guid.NewGuid(), new MatchCriteriaRequest { min_age = 40, max_age = 30 });

            Assert.False(response.issuccessfull);
            Assert.Equal("min_age cannot be greater than max_age.", response.errormessage);
        }

        [Fact]
        public async Task UpdateCriteria_WithNonPositiveRadius_ReturnsFailure()
        {
            var service = CreateService();

            var response = await service.UpdateCriteria(Guid.NewGuid(), new MatchCriteriaRequest { radius_km = 0 });

            Assert.False(response.issuccessfull);
            Assert.Equal("radius_km must be greater than zero.", response.errormessage);
        }

        [Fact]
        public async Task UpdateCriteria_WithRadiusButFeatureNotEnabled_ReturnsFailure()
        {
            var userid = Guid.NewGuid();
            _featureGateService.Setup(f => f.IsEnabled(userid, FeatureKey.RadiusSearchEnabled)).ReturnsAsync(false);

            var service = CreateService();

            var response = await service.UpdateCriteria(userid, new MatchCriteriaRequest { radius_km = 10 });

            Assert.False(response.issuccessfull);
            Assert.Equal("Radius-based search is a premium feature. Upgrade your plan to filter by distance.", response.errormessage);
            _matchCriteriaRepository.Verify(r => r.Upsert(It.IsAny<MatchCriteria>()), Times.Never);
        }

        [Fact]
        public async Task UpdateCriteria_Success_UpsertsAndInvalidatesRecommendationCache()
        {
            var userid = Guid.NewGuid();
            _featureGateService.Setup(f => f.IsEnabled(userid, FeatureKey.RadiusSearchEnabled)).ReturnsAsync(true);
            _matchCriteriaRepository.Setup(r => r.GetByUserId(userid)).ReturnsAsync(new MatchCriteria { userid = userid, city = "Paris", radius_km = 25 });
            _userRepository.Setup(r => r.GetById(userid)).ReturnsAsync(new UserRequest { userid = userid });

            var service = CreateService();

            var response = await service.UpdateCriteria(userid, new MatchCriteriaRequest { city = "Paris", radius_km = 25 });

            Assert.True(response.issuccessfull);
            Assert.Equal("Paris", response.body!.city);
            _matchCriteriaRepository.Verify(r => r.Upsert(It.Is<MatchCriteria>(c => c.userid == userid && c.city == "Paris")), Times.Once);
            _icache.Verify(c => c.Delete($"recommendations:{userid}", CacheRegion.MATCHRECOMMENDATION), Times.Once);
        }

        // --- Swipe (Like/Pass) ---

        [Fact]
        public async Task Like_WithEmptyTargetUserId_ReturnsFailure()
        {
            var service = CreateService();

            var response = await service.Like(Guid.NewGuid(), Guid.Empty);

            Assert.False(response.issuccessfull);
            Assert.Equal("A valid target user is required.", response.errormessage);
        }

        [Fact]
        public async Task Like_OnSelf_ReturnsFailure()
        {
            var userid = Guid.NewGuid();
            var service = CreateService();

            var response = await service.Like(userid, userid);

            Assert.False(response.issuccessfull);
            Assert.Equal("You cannot swipe on yourself.", response.errormessage);
        }

        [Fact]
        public async Task Like_WhenTargetNotFound_ReturnsFailure()
        {
            var userid = Guid.NewGuid();
            var targetUserId = Guid.NewGuid();
            _userRepository.Setup(r => r.GetById(targetUserId)).ReturnsAsync((UserRequest)null!);

            var service = CreateService();

            var response = await service.Like(userid, targetUserId);

            Assert.False(response.issuccessfull);
            Assert.Equal("Target user not found.", response.errormessage);
        }

        [Fact]
        public async Task Like_WhenBlockedEitherDirection_ReturnsFailure()
        {
            var userid = Guid.NewGuid();
            var targetUserId = Guid.NewGuid();
            _userRepository.Setup(r => r.GetById(targetUserId)).ReturnsAsync(new UserRequest { userid = targetUserId });
            _matchRepository.Setup(r => r.IsBlockedEitherDirection(userid, targetUserId)).ReturnsAsync(true);

            var service = CreateService();

            var response = await service.Like(userid, targetUserId);

            Assert.False(response.issuccessfull);
            Assert.Equal("You can't swipe on this user.", response.errormessage);
            _matchRepository.Verify(r => r.RecordSwipe(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<SwipeAction>()), Times.Never);
        }

        [Fact]
        public async Task Like_WhenDailyLikeLimitReached_ReturnsFailureAndNeverRecordsSwipe()
        {
            var userid = Guid.NewGuid();
            var targetUserId = Guid.NewGuid();
            _userRepository.Setup(r => r.GetById(targetUserId)).ReturnsAsync(new UserRequest { userid = targetUserId });
            _matchRepository.Setup(r => r.IsBlockedEitherDirection(userid, targetUserId)).ReturnsAsync(false);
            _featureGateService.Setup(f => f.TryConsume(userid, FeatureKey.DailyLikes, 1))
                .ReturnsAsync(new FeatureConsumeResult { allowed = false, limit = 10, used = 10 });

            var service = CreateService();

            var response = await service.Like(userid, targetUserId);

            Assert.False(response.issuccessfull);
            Assert.Contains("daily like limit", response.errormessage);
            _matchRepository.Verify(r => r.RecordSwipe(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<SwipeAction>()), Times.Never);
        }

        [Fact]
        public async Task Like_NewOneSidedLike_RecordsSwipeAndNotifiesTargetWithoutCreatingMatch()
        {
            var userid = Guid.NewGuid();
            var targetUserId = Guid.NewGuid();
            _userRepository.Setup(r => r.GetById(targetUserId)).ReturnsAsync(new UserRequest { userid = targetUserId });
            _matchRepository.Setup(r => r.IsBlockedEitherDirection(userid, targetUserId)).ReturnsAsync(false);
            _featureGateService.Setup(f => f.TryConsume(userid, FeatureKey.DailyLikes, 1))
                .ReturnsAsync(new FeatureConsumeResult { allowed = true, limit = 10, used = 1 });
            _matchRepository.Setup(r => r.RecordSwipe(userid, targetUserId, SwipeAction.Like))
                .ReturnsAsync((true, (SwipeAction?)null));
            _matchRepository.Setup(r => r.HasLiked(targetUserId, userid)).ReturnsAsync(false);

            var service = CreateService();

            var response = await service.Like(userid, targetUserId);

            Assert.True(response.issuccessfull);
            Assert.False(response.body!.ismatch);
            _notificationService.Verify(n => n.Notify(targetUserId, NotificationType.NewLike, It.IsAny<string>(), It.IsAny<string>(), null), Times.Once);
            _matchRepository.Verify(r => r.CreateMatch(It.IsAny<Guid>(), It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task Like_WhenBothSidesHaveLiked_CreatesMatchAndNotifiesBothParties()
        {
            var userid = Guid.NewGuid();
            var targetUserId = Guid.NewGuid();
            var matchId = Guid.NewGuid();
            _userRepository.Setup(r => r.GetById(targetUserId)).ReturnsAsync(new UserRequest { userid = targetUserId, full_name = "Target" });
            _userRepository.Setup(r => r.GetById(userid)).ReturnsAsync(new UserRequest { userid = userid, full_name = "Me" });
            _matchRepository.Setup(r => r.IsBlockedEitherDirection(userid, targetUserId)).ReturnsAsync(false);
            _featureGateService.Setup(f => f.TryConsume(userid, FeatureKey.DailyLikes, 1))
                .ReturnsAsync(new FeatureConsumeResult { allowed = true, limit = 10, used = 1 });
            _matchRepository.Setup(r => r.RecordSwipe(userid, targetUserId, SwipeAction.Like))
                .ReturnsAsync((true, (SwipeAction?)null));
            _matchRepository.Setup(r => r.HasLiked(targetUserId, userid)).ReturnsAsync(true);
            _matchRepository.Setup(r => r.CreateMatch(userid, targetUserId)).ReturnsAsync((matchId, true));

            var service = CreateService();

            var response = await service.Like(userid, targetUserId);

            Assert.True(response.issuccessfull);
            Assert.True(response.body!.ismatch);
            Assert.Equal(matchId, response.body!.matchid);
            _notificationService.Verify(n => n.Notify(userid, NotificationType.NewMatch, It.IsAny<string>(), It.IsAny<string>(), matchId), Times.Once);
            _notificationService.Verify(n => n.Notify(targetUserId, NotificationType.NewMatch, It.IsAny<string>(), It.IsAny<string>(), matchId), Times.Once);
        }

        [Fact]
        public async Task Like_WhenMatchAlreadyExisted_DoesNotNotifyAgain()
        {
            var userid = Guid.NewGuid();
            var targetUserId = Guid.NewGuid();
            var matchId = Guid.NewGuid();
            _userRepository.Setup(r => r.GetById(targetUserId)).ReturnsAsync(new UserRequest { userid = targetUserId });
            _matchRepository.Setup(r => r.IsBlockedEitherDirection(userid, targetUserId)).ReturnsAsync(false);
            _featureGateService.Setup(f => f.TryConsume(userid, FeatureKey.DailyLikes, 1))
                .ReturnsAsync(new FeatureConsumeResult { allowed = true, limit = 10, used = 1 });
            _matchRepository.Setup(r => r.RecordSwipe(userid, targetUserId, SwipeAction.Like))
                .ReturnsAsync((true, (SwipeAction?)null));
            _matchRepository.Setup(r => r.HasLiked(targetUserId, userid)).ReturnsAsync(true);
            _matchRepository.Setup(r => r.CreateMatch(userid, targetUserId)).ReturnsAsync((matchId, false));

            var service = CreateService();

            var response = await service.Like(userid, targetUserId);

            Assert.True(response.body!.ismatch);
            _notificationService.Verify(n => n.Notify(It.IsAny<Guid>(), NotificationType.NewMatch, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>()), Times.Never);
        }

        [Fact]
        public async Task Like_ReLikeOfAnExistingLike_RefundsTheOptimisticallyChargedUnit()
        {
            var userid = Guid.NewGuid();
            var targetUserId = Guid.NewGuid();
            _userRepository.Setup(r => r.GetById(targetUserId)).ReturnsAsync(new UserRequest { userid = targetUserId });
            _matchRepository.Setup(r => r.IsBlockedEitherDirection(userid, targetUserId)).ReturnsAsync(false);
            _featureGateService.Setup(f => f.TryConsume(userid, FeatureKey.DailyLikes, 1))
                .ReturnsAsync(new FeatureConsumeResult { allowed = true, limit = 10, used = 2 });
            _matchRepository.Setup(r => r.RecordSwipe(userid, targetUserId, SwipeAction.Like))
                .ReturnsAsync((false, SwipeAction.Like));
            _matchRepository.Setup(r => r.HasLiked(targetUserId, userid)).ReturnsAsync(false);

            var service = CreateService();

            await service.Like(userid, targetUserId);

            _featureGateService.Verify(f => f.RefundConsumption(userid, FeatureKey.DailyLikes, 1), Times.Once);
            _notificationService.Verify(n => n.Notify(targetUserId, NotificationType.NewLike, It.IsAny<string>(), It.IsAny<string>(), null), Times.Never);
        }

        [Fact]
        public async Task Pass_DoesNotConsumeDailyLikeQuota()
        {
            var userid = Guid.NewGuid();
            var targetUserId = Guid.NewGuid();
            _userRepository.Setup(r => r.GetById(targetUserId)).ReturnsAsync(new UserRequest { userid = targetUserId });
            _matchRepository.Setup(r => r.IsBlockedEitherDirection(userid, targetUserId)).ReturnsAsync(false);
            _matchRepository.Setup(r => r.RecordSwipe(userid, targetUserId, SwipeAction.Pass))
                .ReturnsAsync((false, (SwipeAction?)null));

            var service = CreateService();

            var response = await service.Pass(userid, targetUserId);

            Assert.True(response.issuccessfull);
            Assert.False(response.body!.ismatch);
            _featureGateService.Verify(f => f.TryConsume(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public async Task Pass_OverwritingAPreviousLike_RefundsThatLikesQuotaCharge()
        {
            var userid = Guid.NewGuid();
            var targetUserId = Guid.NewGuid();
            _userRepository.Setup(r => r.GetById(targetUserId)).ReturnsAsync(new UserRequest { userid = targetUserId });
            _matchRepository.Setup(r => r.IsBlockedEitherDirection(userid, targetUserId)).ReturnsAsync(false);
            _matchRepository.Setup(r => r.RecordSwipe(userid, targetUserId, SwipeAction.Pass))
                .ReturnsAsync((false, SwipeAction.Like));

            var service = CreateService();

            await service.Pass(userid, targetUserId);

            _featureGateService.Verify(f => f.RefundConsumption(userid, FeatureKey.DailyLikes, 1), Times.Once);
        }

        // --- GetMatches ---

        [Fact]
        public async Task GetMatches_ReturnsPagedResultsFromRepository()
        {
            var userid = Guid.NewGuid();
            _matchRepository.Setup(r => r.GetMatches(userid, 0, 50)).ReturnsAsync(new List<MatchSummary>
            {
                new MatchSummary { matchid = Guid.NewGuid(), userid = Guid.NewGuid(), full_name = "Other" }
            });
            _matchRepository.Setup(r => r.CountMatches(userid)).ReturnsAsync(1);

            var service = CreateService();

            var response = await service.GetMatches(userid, new MatchCandidatePageRequest());

            Assert.True(response.issuccessfull);
            Assert.Single(response.body!.results);
            Assert.False(response.body!.has_more);
        }

        // --- Unmatch ---

        [Fact]
        public async Task Unmatch_WhenSuccessful_ReturnsSuccess()
        {
            var userid = Guid.NewGuid();
            var matchId = Guid.NewGuid();
            _matchRepository.Setup(r => r.Unmatch(userid, matchId)).ReturnsAsync(true);

            var service = CreateService();

            var response = await service.Unmatch(userid, matchId);

            Assert.True(response.issuccessfull);
            Assert.Equal("Unmatched", response.body);
        }

        [Fact]
        public async Task Unmatch_WhenCallerIsNotAParticipant_ReturnsFailure()
        {
            var userid = Guid.NewGuid();
            var matchId = Guid.NewGuid();
            _matchRepository.Setup(r => r.Unmatch(userid, matchId)).ReturnsAsync(false);

            var service = CreateService();

            var response = await service.Unmatch(userid, matchId);

            Assert.False(response.issuccessfull);
            Assert.Equal("Match not found.", response.errormessage);
        }

        // --- BlockUser ---

        [Fact]
        public async Task BlockUser_WithEmptyTargetUserId_ReturnsFailure()
        {
            var service = CreateService();

            var response = await service.BlockUser(Guid.NewGuid(), Guid.Empty);

            Assert.False(response.issuccessfull);
            Assert.Equal("A valid target user is required.", response.errormessage);
        }

        [Fact]
        public async Task BlockUser_OnSelf_ReturnsFailure()
        {
            var userid = Guid.NewGuid();
            var service = CreateService();

            var response = await service.BlockUser(userid, userid);

            Assert.False(response.issuccessfull);
            Assert.Equal("You cannot block yourself.", response.errormessage);
        }

        [Fact]
        public async Task BlockUser_WhenTargetDoesNotExist_ReturnsFailure()
        {
            var userid = Guid.NewGuid();
            var targetUserId = Guid.NewGuid();
            _userRepository.Setup(r => r.Exists(targetUserId)).ReturnsAsync(false);

            var service = CreateService();

            var response = await service.BlockUser(userid, targetUserId);

            Assert.False(response.issuccessfull);
            Assert.Equal("Target user not found.", response.errormessage);
            _matchRepository.Verify(r => r.BlockUser(It.IsAny<Guid>(), It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task BlockUser_Success_BlocksAndInvalidatesBothSidesRecommendationCache()
        {
            var userid = Guid.NewGuid();
            var targetUserId = Guid.NewGuid();
            _userRepository.Setup(r => r.Exists(targetUserId)).ReturnsAsync(true);

            var service = CreateService();

            var response = await service.BlockUser(userid, targetUserId);

            Assert.True(response.issuccessfull);
            Assert.Equal("Blocked", response.body);
            _matchRepository.Verify(r => r.BlockUser(userid, targetUserId), Times.Once);
            _icache.Verify(c => c.Delete($"recommendations:{userid}", CacheRegion.MATCHRECOMMENDATION), Times.Once);
            _icache.Verify(c => c.Delete($"recommendations:{targetUserId}", CacheRegion.MATCHRECOMMENDATION), Times.Once);
        }

        // --- GetIncomingLikes ---

        [Fact]
        public async Task GetIncomingLikes_WhenFeatureNotEnabled_ReturnsEmptyUpsellPage()
        {
            var userid = Guid.NewGuid();
            _featureGateService.Setup(f => f.IsEnabled(userid, FeatureKey.WhoLikedMeVisible)).ReturnsAsync(false);

            var service = CreateService();

            var response = await service.GetIncomingLikes(userid, new MatchCandidatePageRequest());

            Assert.True(response.issuccessfull);
            Assert.Empty(response.body!.results);
            Assert.Equal(0, response.body!.total_count);
            _matchRepository.Verify(r => r.GetIncomingLikes(It.IsAny<Guid>(), It.IsAny<MatchCriteria>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public async Task GetIncomingLikes_WhenFeatureEnabled_ReturnsScoredPageFromRepository()
        {
            var userid = Guid.NewGuid();
            _featureGateService.Setup(f => f.IsEnabled(userid, FeatureKey.WhoLikedMeVisible)).ReturnsAsync(true);
            _matchCriteriaRepository.Setup(r => r.GetByUserId(userid)).ReturnsAsync((MatchCriteria)null!);
            _userRepository.Setup(r => r.GetById(userid)).ReturnsAsync(new UserRequest { userid = userid, city = "Austin" });
            _matchRepository.Setup(r => r.GetIncomingLikes(userid, It.IsAny<MatchCriteria>(), 0, 50))
                .ReturnsAsync(new List<UserRequest> { new UserRequest { userid = Guid.NewGuid(), city = "Austin" } });
            _matchRepository.Setup(r => r.CountIncomingLikes(userid, It.IsAny<MatchCriteria>())).ReturnsAsync(1);

            var service = CreateService();

            var response = await service.GetIncomingLikes(userid, new MatchCandidatePageRequest());

            Assert.True(response.issuccessfull);
            Assert.Single(response.body!.results);
            Assert.Equal(1, response.body!.total_count);
        }

        // --- GetSeenProfiles ---

        [Fact]
        public async Task GetSeenProfiles_ReturnsScoredPageFromRepository()
        {
            var userid = Guid.NewGuid();
            _matchCriteriaRepository.Setup(r => r.GetByUserId(userid)).ReturnsAsync((MatchCriteria)null!);
            _userRepository.Setup(r => r.GetById(userid)).ReturnsAsync(new UserRequest { userid = userid });
            _matchRepository.Setup(r => r.GetPassedCandidates(userid, It.IsAny<MatchCriteria>(), 0, 50))
                .ReturnsAsync(new List<UserRequest> { new UserRequest { userid = Guid.NewGuid() } });
            _matchRepository.Setup(r => r.CountPassedCandidates(userid, It.IsAny<MatchCriteria>())).ReturnsAsync(1);

            var service = CreateService();

            var response = await service.GetSeenProfiles(userid, new MatchCandidatePageRequest());

            Assert.True(response.issuccessfull);
            Assert.Single(response.body!.results);
        }

        // --- GetRecommendations ---

        [Fact]
        public async Task GetRecommendations_ColdStart_GrowsBatchFromRepositoryAndCaches()
        {
            var userid = Guid.NewGuid();
            _matchCriteriaRepository.Setup(r => r.GetByUserId(userid)).ReturnsAsync((MatchCriteria)null!);
            _userRepository.Setup(r => r.GetById(userid)).ReturnsAsync(new UserRequest { userid = userid });
            _matchRepository.Setup(r => r.GetCandidates(userid, It.IsAny<MatchCriteria>(), 0, 100))
                .ReturnsAsync(new List<UserRequest> { new UserRequest { userid = Guid.NewGuid() } });
            _matchRepository.Setup(r => r.CountCandidates(userid, It.IsAny<MatchCriteria>())).ReturnsAsync(1);

            var service = CreateService();

            var response = await service.GetRecommendations(userid, new MatchCandidatePageRequest { page = 1, pagesize = 50 });

            Assert.True(response.issuccessfull);
            Assert.Single(response.body!.results);
            Assert.Equal(1, response.body!.total_count);
            _matchRepository.Verify(r => r.GetCandidates(userid, It.IsAny<MatchCriteria>(), 0, 100), Times.Once);
        }

        [Fact]
        public async Task GetRecommendations_WithExistingCachedBatchCoveringPage_SkipsGrowingBatch()
        {
            var userid = Guid.NewGuid();
            var cachedBatch = new MatchRecommendationBatch
            {
                Items = Enumerable.Range(0, 50).Select(i => new MatchCandidateResponse { userid = Guid.NewGuid() }).ToList(),
                NextOffset = 50,
                TotalCount = 50,
                ExhaustedPool = true
            };
            SetupCachedRecommendationBatch(userid, cachedBatch);
            _matchCriteriaRepository.Setup(r => r.GetByUserId(userid)).ReturnsAsync((MatchCriteria)null!);
            _userRepository.Setup(r => r.GetById(userid)).ReturnsAsync(new UserRequest { userid = userid });

            var service = CreateService();

            var response = await service.GetRecommendations(userid, new MatchCandidatePageRequest { page = 1, pagesize = 50 });

            Assert.True(response.issuccessfull);
            Assert.Equal(50, response.body!.results.Count);
            _matchRepository.Verify(r => r.GetCandidates(It.IsAny<Guid>(), It.IsAny<MatchCriteria>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public async Task GetRecommendations_WithRefreshRequested_DeletesCachedBatchBeforeRebuilding()
        {
            var userid = Guid.NewGuid();
            var cachedBatch = new MatchRecommendationBatch
            {
                Items = Enumerable.Range(0, 50).Select(i => new MatchCandidateResponse { userid = Guid.NewGuid() }).ToList(),
                NextOffset = 50,
                TotalCount = 50,
                ExhaustedPool = true
            };
            SetupCachedRecommendationBatch(userid, cachedBatch);
            _matchCriteriaRepository.Setup(r => r.GetByUserId(userid)).ReturnsAsync((MatchCriteria)null!);
            _userRepository.Setup(r => r.GetById(userid)).ReturnsAsync(new UserRequest { userid = userid });

            var service = CreateService();

            await service.GetRecommendations(userid, new MatchCandidatePageRequest { page = 1, pagesize = 50, refresh = true });

            _icache.Verify(c => c.Delete($"recommendations:{userid}", CacheRegion.MATCHRECOMMENDATION), Times.Once);
        }

        // --- WarmRecommendations ---

        [Fact]
        public async Task WarmRecommendations_WhenAlreadyWarm_DoesNotTouchRepository()
        {
            var userid = Guid.NewGuid();
            var cachedBatch = new MatchRecommendationBatch
            {
                Items = new List<MatchCandidateResponse> { new MatchCandidateResponse { userid = Guid.NewGuid() } }
            };
            SetupCachedRecommendationBatch(userid, cachedBatch);

            var service = CreateService();

            await service.WarmRecommendations(userid);

            _matchRepository.Verify(r => r.GetCandidates(It.IsAny<Guid>(), It.IsAny<MatchCriteria>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public async Task WarmRecommendations_WhenNoCachedBatch_GrowsAndCachesOne()
        {
            var userid = Guid.NewGuid();
            _matchCriteriaRepository.Setup(r => r.GetByUserId(userid)).ReturnsAsync((MatchCriteria)null!);
            _userRepository.Setup(r => r.GetById(userid)).ReturnsAsync(new UserRequest { userid = userid });
            _matchRepository.Setup(r => r.GetCandidates(userid, It.IsAny<MatchCriteria>(), 0, 100))
                .ReturnsAsync(new List<UserRequest> { new UserRequest { userid = Guid.NewGuid() } });
            _matchRepository.Setup(r => r.CountCandidates(userid, It.IsAny<MatchCriteria>())).ReturnsAsync(1);

            var service = CreateService();

            await service.WarmRecommendations(userid);

            _matchRepository.Verify(r => r.GetCandidates(userid, It.IsAny<MatchCriteria>(), 0, 100), Times.Once);
            _icache.Verify(c => c.Set($"recommendations:{userid}", It.IsAny<CacheItem<MatchRecommendationBatch>>(), CacheRegion.MATCHRECOMMENDATION, It.IsAny<TimeSpan?>(), It.IsAny<ExpirationMode>()), Times.Once);
        }

        [Fact]
        public async Task WarmRecommendations_WhenRepositoryThrows_SwallowsExceptionRatherThanPropagating()
        {
            var userid = Guid.NewGuid();
            _matchCriteriaRepository.Setup(r => r.GetByUserId(userid)).ThrowsAsync(new Exception("db unavailable"));

            var service = CreateService();

            await service.WarmRecommendations(userid);
        }
    }
}
