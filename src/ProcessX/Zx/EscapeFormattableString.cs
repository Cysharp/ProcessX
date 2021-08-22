using System;

namespace Zx
{
    internal static class EscapeFormattableString
    {
        internal static string Escape(FormattableString formattableString)
        {
            // already escaped.
            if (formattableString.Format.StartsWith("\"") && formattableString.Format.EndsWith("\""))
            {
                return formattableString.ToString();
            }

            // GetArguments returns inner object[] field, it can modify.
            var args = formattableString.GetArguments();

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] is string s)
                {
                    if (!s.Contains(" "))
                    {
                        continue; // no need for escape
                    }

                    args[i] = "\"" + args[i].ToString().Replace("\"", "\\\"") + "\""; // poor logic
                }
            }

            return formattableString.ToString();
        }
    }
}