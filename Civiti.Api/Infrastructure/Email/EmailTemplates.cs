using System.Net;
using Civiti.Application.Email.Models;
using static Civiti.Api.Infrastructure.Email.EmailDataKeys;

namespace Civiti.Api.Infrastructure.Email;

/// <summary>
/// Static Romanian email templates for all 14 notification types.
/// Each method returns (subject, bodyHtml) for a given template data dictionary.
/// </summary>
public static class EmailTemplates
{
    /// <summary>
    /// HTML-encodes a string to prevent injection in email content.
    /// </summary>
    private static string H(string? value) => WebUtility.HtmlEncode(value ?? "");
    public static (string Subject, string BodyHtml) Get(EmailNotificationType type, Dictionary<string, string> data)
    {
        return type switch
        {
            EmailNotificationType.IssueSubmitted => IssueSubmitted(data),
            EmailNotificationType.IssueApproved => IssueApproved(data),
            EmailNotificationType.IssueRejected => IssueRejected(data),
            EmailNotificationType.ChangesRequested => ChangesRequested(data),
            EmailNotificationType.IssueResolved => IssueResolved(data),
            EmailNotificationType.IssueCancelled => IssueCancelled(data),
            EmailNotificationType.NewCommentOnIssue => NewCommentOnIssue(data),
            EmailNotificationType.ReplyToComment => ReplyToComment(data),
            EmailNotificationType.VoteMilestone => VoteMilestone(data),
            EmailNotificationType.EmailSupportMilestone => EmailSupportMilestone(data),
            EmailNotificationType.LevelUp => LevelUp(data),
            EmailNotificationType.BadgeEarned => BadgeEarned(data),
            EmailNotificationType.AchievementCompleted => AchievementCompleted(data),
            EmailNotificationType.Welcome => Welcome(data),
            EmailNotificationType.AdminNewIssue => AdminNewIssue(data),
            _ => ("Notificare Civiti", "<p>Ai o notificare noua pe Civiti.</p>")
        };
    }

    private static (string, string) IssueSubmitted(Dictionary<string, string> d)
    {
        var title = H(d.GetValueOrDefault(IssueTitle, ""));
        var userName = H(d.GetValueOrDefault(UserName, ""));
        return (
            "Problema ta a fost trimisa",
            "<p>Salut, <strong>" + userName + "</strong>!</p>" +
            "<p>Problema ta <strong>" + title + "</strong> a fost trimisa cu succes si este acum in asteptarea revizuirii de catre echipa noastra de moderare.</p>" +
            "<p>Vei primi o notificare cand problema ta va fi revizuita.</p>"
        );
    }

    private static (string, string) IssueApproved(Dictionary<string, string> d)
    {
        var title = H(d.GetValueOrDefault(IssueTitle, ""));
        var userName = H(d.GetValueOrDefault(UserName, ""));
        return (
            "Problema ta a fost aprobata!",
            "<p>Vesti bune, <strong>" + userName + "</strong>!</p>" +
            "<p>Problema ta <strong>" + title + "</strong> a fost aprobata si este acum vizibila comunitatii.</p>" +
            "<p>Ai primit <strong style=\"color: #FCA311;\">+50 puncte</strong> pentru contributia ta!</p>" +
            "<p>Comunitatea poate acum sa voteze si sa trimita emailuri de sustinere catre autoritati.</p>"
        );
    }

    private static (string, string) IssueRejected(Dictionary<string, string> d)
    {
        var title = H(d.GetValueOrDefault(IssueTitle, ""));
        var userName = H(d.GetValueOrDefault(UserName, ""));
        var reason = H(d.GetValueOrDefault(Reason, "Nu a fost specificat un motiv."));
        return (
            "Problema ta nu a fost aprobata",
            "<p>Salut, <strong>" + userName + "</strong>.</p>" +
            "<p>Din pacate, problema ta <strong>" + title + "</strong> nu a fost aprobata de echipa de moderare.</p>" +
            "<p><strong>Motivul:</strong> " + reason + "</p>" +
            "<p>Poti crea o noua problema tinand cont de feedback-ul primit.</p>"
        );
    }

    private static (string, string) ChangesRequested(Dictionary<string, string> d)
    {
        var title = H(d.GetValueOrDefault(IssueTitle, ""));
        var userName = H(d.GetValueOrDefault(UserName, ""));
        var notes = H(d.GetValueOrDefault(Notes, ""));
        return (
            "Modificari solicitate pentru problema ta",
            "<p>Salut, <strong>" + userName + "</strong>.</p>" +
            "<p>Echipa de moderare a solicitat modificari la problema ta <strong>" + title + "</strong>.</p>" +
            "<p><strong>Note de la moderator:</strong></p>" +
            "<div style=\"background-color: #F5F5F5; padding: 12px 16px; border-left: 3px solid #FCA311; border-radius: 4px; margin: 12px 0;\">" +
            notes +
            "</div>" +
            "<p>Te rugam sa editezi problema si sa o retrimiti.</p>"
        );
    }

    private static (string, string) IssueResolved(Dictionary<string, string> d)
    {
        var title = H(d.GetValueOrDefault(IssueTitle, ""));
        var userName = H(d.GetValueOrDefault(UserName, ""));
        return (
            "Problema a fost rezolvata!",
            "<p>Salut, <strong>" + userName + "</strong>!</p>" +
            "<p>Problema <strong>" + title + "</strong> a fost marcata ca rezolvata.</p>" +
            "<p>Multumim tuturor celor care au contribuit la rezolvarea acestei probleme!</p>"
        );
    }

    private static (string, string) IssueCancelled(Dictionary<string, string> d)
    {
        var title = H(d.GetValueOrDefault(IssueTitle, ""));
        var userName = H(d.GetValueOrDefault(UserName, ""));
        return (
            "Problema a fost anulata",
            "<p>Salut, <strong>" + userName + "</strong>.</p>" +
            "<p>Problema <strong>" + title + "</strong> la care ai participat a fost anulata de autorul ei.</p>"
        );
    }

    private static (string, string) NewCommentOnIssue(Dictionary<string, string> d)
    {
        var title = H(d.GetValueOrDefault(IssueTitle, ""));
        var userName = H(d.GetValueOrDefault(UserName, ""));
        var commenterName = H(d.GetValueOrDefault(CommenterName, "Cineva"));
        var excerpt = H(d.GetValueOrDefault(CommentExcerpt, ""));
        return (
            "Comentariu nou la problema ta",
            "<p>Salut, <strong>" + userName + "</strong>!</p>" +
            "<p><strong>" + commenterName + "</strong> a lasat un comentariu la problema ta <strong>" + title + "</strong>:</p>" +
            "<div style=\"background-color: #F5F5F5; padding: 12px 16px; border-left: 3px solid #14213D; border-radius: 4px; margin: 12px 0; font-style: italic;\">" +
            excerpt +
            "</div>"
        );
    }

    private static (string, string) ReplyToComment(Dictionary<string, string> d)
    {
        var userName = H(d.GetValueOrDefault(UserName, ""));
        var replierName = H(d.GetValueOrDefault(ReplierName, "Cineva"));
        var excerpt = H(d.GetValueOrDefault(ReplyExcerpt, ""));
        return (
            "Raspuns la comentariul tau",
            "<p>Salut, <strong>" + userName + "</strong>!</p>" +
            "<p><strong>" + replierName + "</strong> a raspuns la comentariul tau:</p>" +
            "<div style=\"background-color: #F5F5F5; padding: 12px 16px; border-left: 3px solid #14213D; border-radius: 4px; margin: 12px 0; font-style: italic;\">" +
            excerpt +
            "</div>"
        );
    }

    private static (string, string) VoteMilestone(Dictionary<string, string> d)
    {
        var title = H(d.GetValueOrDefault(IssueTitle, ""));
        var userName = H(d.GetValueOrDefault(UserName, ""));
        var rawCount = d.GetValueOrDefault(VoteCount, "0");
        var count = H(rawCount);
        return (
            "Problema ta a atins " + rawCount + " voturi!",
            "<p>Felicitari, <strong>" + userName + "</strong>!</p>" +
            "<p>Problema ta <strong>" + title + "</strong> a atins un nou prag: <strong style=\"color: #FCA311;\">" + count + " voturi</strong> din partea comunitatii!</p>" +
            "<p>Continua sa faci diferenta in comunitatea ta.</p>"
        );
    }

    private static (string, string) EmailSupportMilestone(Dictionary<string, string> d)
    {
        var title = H(d.GetValueOrDefault(IssueTitle, ""));
        var userName = H(d.GetValueOrDefault(UserName, ""));
        var rawCount = d.GetValueOrDefault(EmailCount, "0");
        var count = H(rawCount);
        return (
            "Problema ta: " + rawCount + " emailuri trimise!",
            "<p>Felicitari, <strong>" + userName + "</strong>!</p>" +
            "<p>Comunitatea a trimis <strong style=\"color: #FCA311;\">" + count + " emailuri</strong> catre autoritati in sprijinul problemei tale <strong>" + title + "</strong>!</p>" +
            "<p>Presiunea civica functioneaza!</p>"
        );
    }

    private static (string, string) LevelUp(Dictionary<string, string> d)
    {
        var userName = H(d.GetValueOrDefault(UserName, ""));
        var rawLevel = d.GetValueOrDefault(Level, "");
        var level = H(rawLevel);
        return (
            "Ai avansat la nivelul " + rawLevel + "!",
            "<p>Felicitari, <strong>" + userName + "</strong>!</p>" +
            "<p>Ai atins <strong style=\"color: #FCA311;\">Nivelul " + level + "</strong> pe Civiti!</p>" +
            "<p>Continua activitatea civica pentru a avansa si mai mult.</p>"
        );
    }

    private static (string, string) BadgeEarned(Dictionary<string, string> d)
    {
        var userName = H(d.GetValueOrDefault(UserName, ""));
        var rawBadgeName = d.GetValueOrDefault(BadgeName, "");
        var badgeName = H(rawBadgeName);
        return (
            "Ai castigat insigna " + rawBadgeName + "!",
            "<p>Felicitari, <strong>" + userName + "</strong>!</p>" +
            "<p>Ai castigat o insigna noua: <strong style=\"color: #FCA311;\">" + badgeName + "</strong></p>" +
            "<p>Viziteaza profilul tau pentru a vedea toate insignele castigate.</p>"
        );
    }

    private static (string, string) AchievementCompleted(Dictionary<string, string> d)
    {
        var userName = H(d.GetValueOrDefault(UserName, ""));
        var rawAchievementName = d.GetValueOrDefault(AchievementName, "");
        var achievementName = H(rawAchievementName);
        return (
            "Realizare completata: " + rawAchievementName + "!",
            "<p>Felicitari, <strong>" + userName + "</strong>!</p>" +
            "<p>Ai completat realizarea: <strong style=\"color: #FCA311;\">" + achievementName + "</strong></p>" +
            "<p>Descopera ce alte realizari te asteapta pe Civiti.</p>"
        );
    }

    private static (string, string) AdminNewIssue(Dictionary<string, string> d)
    {
        var rawTitle = d.GetValueOrDefault(IssueTitle, "");
        var title = H(rawTitle);
        var category = H(d.GetValueOrDefault(EmailDataKeys.IssueCategory, "-"));
        var address = H(d.GetValueOrDefault(EmailDataKeys.IssueAddress, "-"));
        var urgency = H(d.GetValueOrDefault(EmailDataKeys.IssueUrgency, "-"));
        var submitter = H(d.GetValueOrDefault(UserName, "Anonim"));

        return (
            "Nouă problemă raportată: " + rawTitle,
            "<p>O nouă problemă a fost trimisă pe Civiti și așteaptă revizuire.</p>" +
            "<table style=\"border-collapse: collapse; margin: 16px 0; font-size: 14px;\">" +
            "<tr><td style=\"padding: 4px 12px 4px 0; color: #6B7280;\">Titlu</td>" +
            "<td style=\"padding: 4px 0;\"><strong>" + title + "</strong></td></tr>" +
            "<tr><td style=\"padding: 4px 12px 4px 0; color: #6B7280;\">Categorie</td>" +
            "<td style=\"padding: 4px 0;\">" + category + "</td></tr>" +
            "<tr><td style=\"padding: 4px 12px 4px 0; color: #6B7280;\">Adresă</td>" +
            "<td style=\"padding: 4px 0;\">" + address + "</td></tr>" +
            "<tr><td style=\"padding: 4px 12px 4px 0; color: #6B7280;\">Urgență</td>" +
            "<td style=\"padding: 4px 0;\">" + urgency + "</td></tr>" +
            "<tr><td style=\"padding: 4px 12px 4px 0; color: #6B7280;\">Raportată de</td>" +
            "<td style=\"padding: 4px 0;\">" + submitter + "</td></tr>" +
            "</table>" +
            "<p>Te rugăm să deschizi panoul admin pentru a o revizui.</p>"
        );
    }

    private static (string, string) Welcome(Dictionary<string, string> d)
    {
        var userName = H(d.GetValueOrDefault(UserName, ""));
        return (
            "Bine ai venit pe Civiti!",
            "<p>Salut, <strong>" + userName + "</strong>!</p>" +
            "<p>Iti multumim ca te-ai alaturat comunitatii <strong>Civiti</strong> — platforma civica a cetatenilor.</p>" +
            "<p>Iata ce poti face pe Civiti:</p>" +
            "<ul style=\"padding-left: 20px; margin: 12px 0;\">" +
            "<li style=\"margin-bottom: 8px;\"><strong>Raporteaza probleme</strong> din comunitatea ta</li>" +
            "<li style=\"margin-bottom: 8px;\"><strong>Voteaza</strong> problemele care te afecteaza</li>" +
            "<li style=\"margin-bottom: 8px;\"><strong>Trimite emailuri</strong> catre autoritati pentru a exercita presiune civica</li>" +
            "<li style=\"margin-bottom: 8px;\"><strong>Castiga puncte si insigne</strong> pentru activitatea ta civica</li>" +
            "</ul>" +
            "<p>Impreuna putem face diferenta!</p>"
        );
    }
}
