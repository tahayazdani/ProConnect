namespace ProConnect.Domain.Exception
{
    /// <summary>
    /// Thrown by UserRepository.SignUp when a DB-level unique-constraint violation on `sub` or
    /// `emailaddress` is detected (Postgres 23505) - defense-in-depth against a true concurrent-request
    /// race, where two genuinely simultaneous signups for the same new sub/email both pass
    /// UserService.SignUp's proactive GetBySubForAuthentication/GetByEmailForAuthentication check
    /// before either one has committed its insert. ConflictField tells the caller which axis collided
    /// ("sub" or "email"), so UserService.SignUp can return the exact same clean, specific message its
    /// proactive check would have returned had the race not occurred, instead of letting a raw Npgsql
    /// exception escape as an unhandled 500.
    /// </summary>
    public class DuplicateUserException : System.Exception
    {
        public string ConflictField { get; }

        public DuplicateUserException(string conflictField)
            : base($"A duplicate user write was rejected by the database on '{conflictField}'.")
        {
            ConflictField = conflictField;
        }
    }
}
