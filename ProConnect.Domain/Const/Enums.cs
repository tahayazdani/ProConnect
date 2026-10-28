using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProConnect.Domain.Const
{
    public class Enums
    {

    }

    public enum ErrorCode
    {
        OK = 200,

        USERISEXIST = 300,
        USERISNOTEXIST = 301,

        VALIDATION = 450,
        INVALIDEMAILADDRESS = 451,
        NOTADULT = 452,
        INVALIDDATEFORMAT = 453,

        SYSTEM = 500
    }

    public enum SwipeAction
    {
        Like,
        Pass
    }

    /// <summary>Lifecycle state of a UserSubscriptionEntity row - see IFeatureGateService/SubscriptionService.</summary>
    public enum SubscriptionStatus
    {
        // No row at all (brand-new user) also behaves as Free - see SubscriptionRepository.GetUserSubscription.
        Active,
        Canceled,
        PastDue,
        Expired
    }

    public enum BillingInterval
    {
        Monthly,
        Annual
    }

    /// <summary>
    /// How often a counted PlanFeatureEntity.limit_value resets - see IFeatureGateService.TryConsume's
    /// period-key derivation. None means the feature is a plain on/off flag (limit_value is always null).
    /// </summary>
    public enum ResetPeriod
    {
        None,
        Daily,
        Monthly
    }

    public enum PaymentTransactionType
    {
        Subscription,
        Consumable
    }

    public enum PaymentStatus
    {
        Pending,
        Succeeded,
        Failed,
        Refunded
    }

    /// <summary>
    /// Kinds of events a NotificationEntity row can represent - see INotificationService for where each one
    /// is raised (MatchService.Swipe for NewMatch/NewLike, SubscriptionService's webhook handlers for the
    /// rest) and NotificationChannelConfigEntity for the per-type/per-channel "is this actually sent" gate.
    /// A closed enum (unlike Const.FeatureKey's deliberately-open strings) since, unlike a plan's feature
    /// set, the call sites that raise a notification are fixed/known at compile time - there's no "add a row,
    /// no code change" story for a notification type the way there is for a plan_feature.
    /// </summary>
    public enum NotificationType
    {
        NewMatch,
        NewLike,
        SubscriptionActivated,
        SubscriptionRenewed,
        PaymentFailed,
        SubscriptionCanceled,
        // Raised once, by UserService.SignUp, right after a brand-new account is created - the one
        // NotificationType that is campaign/email-first rather than in-app-first (a brand-new user has no
        // "notifications list" to check yet). Seeded Email = true (unlike every pre-existing type, which
        // seeds Email = false) - see NotificationChannelConfigEntityConfiguration - because this is the
        // whole reason IEmailGateway/IEmailTemplateRenderer exist: there is no point shipping a welcome
        // email feature that is seeded off.
        Welcome
    }

    /// <summary>
    /// Delivery channel a NotificationType can additionally be sent over, gated per-(type, channel) by
    /// NotificationChannelConfigEntity.is_enabled - "flip a flag, no redeploy", same philosophy as
    /// plan_feature. The NotificationEntity row itself (surfaced via NotificationController's GET endpoints)
    /// always gets created regardless of this config; Push/Email are additive pushes on top of that.
    /// Email is seeded disabled (see NotificationChannelConfigEntityConfiguration) - the config knob exists
    /// today so turning it on later is a DB flip, not a deploy, but no email-sending gateway is wired up yet
    /// (same "built but dark" story as everything behind IPaymentGateway before real Stripe keys existed).
    /// </summary>
    public enum NotificationChannel
    {
        Push,
        Email
    }

    /// <summary>Platform a UserDeviceTokenEntity token was registered from. FCM (IPushNotificationGateway)
    /// covers all three - Web Push, Android, and iOS via FCM's APNs bridge - through one unified send call,
    /// so nothing above that seam needs to branch on this; it exists purely so a user's device list
    /// (web + phone) can be displayed/managed and so expired tokens can eventually be pruned per-platform.</summary>
    public enum DevicePlatform
    {
        Web,
        IOS,
        Android
    }
}
