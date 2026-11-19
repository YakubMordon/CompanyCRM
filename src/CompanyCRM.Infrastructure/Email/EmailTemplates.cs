using System.Net;
using CompanyCRM.Domain.Enums;

namespace CompanyCRM.Infrastructure.Email;

public static class EmailTemplates
{
    public static string GenerateReviewAssignedHtml(
        string recipientName,
        string workItemCode,
        string workItemTitle,
        Guid workItemId)
    {
        var safeName = WebUtility.HtmlEncode(recipientName);
        var safeCode = WebUtility.HtmlEncode(workItemCode);
        var safeTitle = WebUtility.HtmlEncode(workItemTitle);

        return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"" />
    <style>
        body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background-color: #f4f6f8; margin: 0; padding: 20px; }}
        .card {{ background: #ffffff; border-radius: 8px; max-width: 600px; margin: 0 auto; padding: 32px; border: 1px solid #e2e8f0; }}
        .header {{ border-bottom: 2px solid #3b82f6; padding-bottom: 16px; margin-bottom: 24px; }}
        .title {{ font-size: 20px; font-weight: 700; color: #1e293b; margin: 0; }}
        .badge {{ display: inline-block; padding: 4px 10px; border-radius: 9999px; font-size: 12px; font-weight: 600; background: #e0f2fe; color: #0284c7; margin-bottom: 12px; }}
        .content {{ color: #475569; font-size: 15px; line-height: 1.6; }}
        .item-box {{ background: #f8fafc; border: 1px solid #e2e8f0; border-radius: 6px; padding: 16px; margin: 20px 0; }}
        .btn {{ display: inline-block; background-color: #2563eb; color: #ffffff !important; text-decoration: none; padding: 12px 24px; border-radius: 6px; font-weight: 600; font-size: 14px; margin-top: 16px; }}
        .footer {{ margin-top: 32px; padding-top: 16px; border-top: 1px solid #e2e8f0; font-size: 12px; color: #94a3b8; text-align: center; }}
    </style>
</head>
<body>
    <div class=""card"">
        <div class=""header"">
            <span class=""badge"">Review Requested</span>
            <h1 class=""title"">Work Item Review Assigned</h1>
        </div>
        <div class=""content"">
            <p>Hello <strong>{safeName}</strong>,</p>
            <p>You have been assigned as the reviewer for the following work item:</p>
            <div class=""item-box"">
                <div style=""font-size: 13px; color: #64748b; font-weight: 600;"">{safeCode}</div>
                <div style=""font-size: 16px; color: #0f172a; font-weight: 700; margin-top: 4px;"">{safeTitle}</div>
            </div>
            <p>Please inspect the acceptance criteria, implementation notes, and provide your approval decision in the Review Queue.</p>
            <a href=""/reviews"" class=""btn"">Open Review Queue</a>
        </div>
        <div class=""footer"">
            CompanyCRM Automated Notification System &bull; Confidential
        </div>
    </div>
</body>
</html>";
    }

    public static string GenerateReviewDecisionHtml(
        string recipientName,
        string workItemCode,
        string workItemTitle,
        ReviewStatus status,
        string? comment)
    {
        var safeName = WebUtility.HtmlEncode(recipientName);
        var safeCode = WebUtility.HtmlEncode(workItemCode);
        var safeTitle = WebUtility.HtmlEncode(workItemTitle);
        var safeComment = WebUtility.HtmlEncode(comment ?? string.Empty);

        string badgeText;
        string badgeBg;
        string badgeColor;
        string statusHeadline;
        string calloutHtml = string.Empty;

        switch (status)
        {
            case ReviewStatus.Approved:
                badgeText = "Review Approved";
                badgeBg = "#dcfce7";
                badgeColor = "#16a34a";
                statusHeadline = "Your work item review has been approved!";
                if (!string.IsNullOrWhiteSpace(comment))
                {
                    calloutHtml = $@"
                    <div style=""background: #f0fdf4; border-left: 4px solid #16a34a; padding: 12px; margin: 16px 0; border-radius: 4px;"">
                        <strong>Approval Note:</strong> {safeComment}
                    </div>";
                }
                break;

            case ReviewStatus.ClarificationNeeded:
                badgeText = "Clarification Needed";
                badgeBg = "#fef3c7";
                badgeColor = "#d97706";
                statusHeadline = "The reviewer requested clarification on your work item.";
                calloutHtml = $@"
                <div style=""background: #fffbeb; border-left: 4px solid #f59e0b; padding: 16px; margin: 16px 0; border-radius: 4px;"">
                    <strong style=""color: #b45309;"">Reviewer Question / Comment:</strong>
                    <p style=""margin: 8px 0 0 0; color: #78350f;"">{safeComment}</p>
                </div>";
                break;

            case ReviewStatus.Denied:
                badgeText = "Review Denied";
                badgeBg = "#fee2e2";
                badgeColor = "#dc2626";
                statusHeadline = "Your work item review was denied. External synchronization is blocked.";
                calloutHtml = $@"
                <div style=""background: #fef2f2; border-left: 4px solid #ef4444; padding: 16px; margin: 16px 0; border-radius: 4px;"">
                    <strong style=""color: #b91c1c;"">Denial Reason:</strong>
                    <p style=""margin: 8px 0 0 0; color: #7f1d1d;"">{safeComment}</p>
                </div>
                <p style=""color: #dc2626; font-size: 13px; font-weight: 600;"">Note: This item cannot be pushed to external issue trackers while in Denied status.</p>";
                break;

            default:
                badgeText = "Review Update";
                badgeBg = "#f1f5f9";
                badgeColor = "#475569";
                statusHeadline = $"Work item review updated to {status}.";
                break;
        }

        return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"" />
    <style>
        body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background-color: #f4f6f8; margin: 0; padding: 20px; }}
        .card {{ background: #ffffff; border-radius: 8px; max-width: 600px; margin: 0 auto; padding: 32px; border: 1px solid #e2e8f0; }}
        .header {{ border-bottom: 2px solid {badgeColor}; padding-bottom: 16px; margin-bottom: 24px; }}
        .title {{ font-size: 20px; font-weight: 700; color: #1e293b; margin: 0; }}
        .badge {{ display: inline-block; padding: 4px 10px; border-radius: 9999px; font-size: 12px; font-weight: 600; background: {badgeBg}; color: {badgeColor}; margin-bottom: 12px; }}
        .content {{ color: #475569; font-size: 15px; line-height: 1.6; }}
        .item-box {{ background: #f8fafc; border: 1px solid #e2e8f0; border-radius: 6px; padding: 16px; margin: 20px 0; }}
        .btn {{ display: inline-block; background-color: #2563eb; color: #ffffff !important; text-decoration: none; padding: 12px 24px; border-radius: 6px; font-weight: 600; font-size: 14px; margin-top: 16px; }}
        .footer {{ margin-top: 32px; padding-top: 16px; border-top: 1px solid #e2e8f0; font-size: 12px; color: #94a3b8; text-align: center; }}
    </style>
</head>
<body>
    <div class=""card"">
        <div class=""header"">
            <span class=""badge"">{badgeText}</span>
            <h1 class=""title"">{statusHeadline}</h1>
        </div>
        <div class=""content"">
            <p>Hello <strong>{safeName}</strong>,</p>
            <div class=""item-box"">
                <div style=""font-size: 13px; color: #64748b; font-weight: 600;"">{safeCode}</div>
                <div style=""font-size: 16px; color: #0f172a; font-weight: 700; margin-top: 4px;"">{safeTitle}</div>
            </div>
            {calloutHtml}
            <a href=""/reviews"" class=""btn"">View Work Item</a>
        </div>
        <div class=""footer"">
            CompanyCRM Automated Notification System &bull; Confidential
        </div>
    </div>
</body>
</html>";
    }

    public static string GenerateCutoffReminderHtml(
        string recipientName,
        DateOnly planDate,
        TimeOnly cutoffTime)
    {
        var safeName = WebUtility.HtmlEncode(recipientName);

        return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"" />
    <style>
        body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background-color: #f4f6f8; margin: 0; padding: 20px; }}
        .card {{ background: #ffffff; border-radius: 8px; max-width: 600px; margin: 0 auto; padding: 32px; border: 1px solid #e2e8f0; }}
        .header {{ border-bottom: 2px solid #eab308; padding-bottom: 16px; margin-bottom: 24px; }}
        .title {{ font-size: 20px; font-weight: 700; color: #1e293b; margin: 0; }}
        .badge {{ display: inline-block; padding: 4px 10px; border-radius: 9999px; font-size: 12px; font-weight: 600; background: #fef9c3; color: #854d0e; margin-bottom: 12px; }}
        .content {{ color: #475569; font-size: 15px; line-height: 1.6; }}
        .warning-box {{ background: #fefce8; border: 1px solid #fef08a; border-radius: 6px; padding: 16px; margin: 20px 0; color: #713f12; }}
        .btn {{ display: inline-block; background-color: #ca8a04; color: #ffffff !important; text-decoration: none; padding: 12px 24px; border-radius: 6px; font-weight: 600; font-size: 14px; margin-top: 16px; }}
        .footer {{ margin-top: 32px; padding-top: 16px; border-top: 1px solid #e2e8f0; font-size: 12px; color: #94a3b8; text-align: center; }}
    </style>
</head>
<body>
    <div class=""card"">
        <div class=""header"">
            <span class=""badge"">Action Required</span>
            <h1 class=""title"">Missing Daily Planning Check-in</h1>
        </div>
        <div class=""content"">
            <p>Hello <strong>{safeName}</strong>,</p>
            <div class=""warning-box"">
                <strong>Notice:</strong> Your daily plan for <strong>{planDate:yyyy-MM-dd}</strong> has not been submitted. The daily cutoff deadline of <strong>{cutoffTime:HH:mm}</strong> has passed.
            </div>
            <p>Please submit your planned tasks, hours, and any blockers for today as soon as possible so your team lead has visibility into sprint progress.</p>
            <a href=""/planning"" class=""btn"">Submit Daily Plan Now</a>
        </div>
        <div class=""footer"">
            CompanyCRM Automated Notification System &bull; Confidential
        </div>
    </div>
</body>
</html>";
    }
}
