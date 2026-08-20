namespace TrenchBroom.Companion.Core;

public sealed class CompanionProjectCreationResult
{
    internal CompanionProjectCreationResult(
        CompanionProjectSession session,
        CompanionProvisionedProject provisionedProject)
    {
        Session =
            session ??
            throw new ArgumentNullException(
                nameof(session));

        ProvisionedProject =
            provisionedProject ??
            throw new ArgumentNullException(
                nameof(provisionedProject));
    }

    public CompanionProjectSession Session { get; }

    public CompanionProvisionedProject ProvisionedProject { get; }
}
