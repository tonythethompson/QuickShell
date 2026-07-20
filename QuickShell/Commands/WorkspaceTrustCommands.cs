using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Services;

namespace QuickShell.Commands;

internal sealed partial class GrantWorkspaceTrustCommand : InvokableCommand
{
    private readonly IQuickShellServices _services;
    private readonly string _workspaceId;
    private readonly WorkspaceReviewToken? _reviewToken;
    private readonly Action _onChanged;

    public GrantWorkspaceTrustCommand(
        string workspaceId,
        Action onChanged,
        IQuickShellServices services,
        WorkspaceReviewToken? reviewToken = null)
    {
        _workspaceId = workspaceId;
        _onChanged = onChanged;
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _reviewToken = reviewToken;
        Name = "Trust workspace";
        Icon = new IconInfo("\uE72E");
    }

    public override CommandResult Invoke()
    {
        if (_reviewToken is not null)
        {
            var transition = _services.Shortcuts.GrantTrust(_workspaceId, _reviewToken);
            if (transition.Status == TrustTransitionStatus.WorkspaceChangedSinceReview)
            {
                return QuickShellNavigation.StayOpen(transition.Message);
            }

            _onChanged();
            return QuickShellNavigation.StayOpen(transition.Message);
        }

        var review = _services.Shortcuts.BeginTrustReview(_workspaceId);
        if (review.Workspace is null)
        {
            return QuickShellNavigation.StayOpen("Workspace was not found.");
        }

        if (!review.Assessment.IsAllowed || review.Token is null)
        {
            var issue = review.Assessment.PrimaryIssueCode?.ToString() ?? "invalid content";
            return QuickShellNavigation.StayOpen($"Repair this workspace before trusting it ({issue}).");
        }

        var risks = review.Assessment.Risks.Count == 0
            ? "No command, elevation, companion, or URL risks were detected."
            : string.Join(" ", review.Assessment.Risks.Select(risk => risk.Description));
        return CommandResult.Confirm(new ConfirmationArgs
        {
            Title = "Trust workspace?",
            Description = "Trust applies to this editable local workspace. It can execute arbitrary code, and later command or launch-setting edits remain trusted until you revoke trust. " + risks,
            PrimaryCommand = new GrantWorkspaceTrustCommand(
                _workspaceId,
                _onChanged,
                _services,
                review.Token),
        });
    }
}

internal sealed partial class RevokeWorkspaceTrustCommand : InvokableCommand
{
    private readonly IQuickShellServices _services;
    private readonly string _workspaceId;
    private readonly Action _onChanged;

    public RevokeWorkspaceTrustCommand(string workspaceId, Action onChanged, IQuickShellServices services)
    {
        _workspaceId = workspaceId;
        _onChanged = onChanged;
        _services = services ?? throw new ArgumentNullException(nameof(services));
        Name = "Revoke workspace trust";
        Icon = new IconInfo("\uE72E");
    }

    public override CommandResult Invoke()
    {
        var transition = _services.Shortcuts.RevokeTrust(_workspaceId);
        _onChanged();
        return QuickShellNavigation.StayOpen(transition.Message);
    }
}

