namespace Civiti.Domain.Exceptions;

/// <summary>
/// An issue's photos or authorities broke a rule that the request DTO cannot express on its own
/// — cross-entry uniqueness, the predefined-or-custom exclusivity, or a reference to an
/// authority that does not exist.
/// <para>
/// Derives from <see cref="InvalidOperationException"/> so the existing create-path handlers,
/// which map that family to <c>400</c>, keep working unchanged; the distinct type lets the edit
/// path tell a caller's mistake apart from an internal fault it should surface as a <c>500</c>.
/// </para>
/// </summary>
public class IssueContentValidationException(string message) : InvalidOperationException(message);
