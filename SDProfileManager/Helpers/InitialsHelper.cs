namespace SDProfileManager.Helpers;

public static class InitialsHelper
{
    public static string GetInitials(string name, int maxLength = 2)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "?";

        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0) return "?";

        var initials = string.Concat(words.Take(maxLength).Select(w => char.ToUpper(w[0])));
        return string.IsNullOrEmpty(initials) ? "?" : initials;
    }
}
