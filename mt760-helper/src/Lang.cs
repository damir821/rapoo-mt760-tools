using System.Globalization;

namespace MT760Helper
{
    // Interface language. Texts are written in place as Lang.T("русский", "English").
    static class Lang
    {
        public static bool English;

        // setting: "ru", "en" or "auto" (Russian when Windows is in Russian, English otherwise).
        public static void Apply(string setting)
        {
            English = setting == "en" ||
                      (setting != "ru" && CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "ru");
        }

        public static string T(string ru, string en)
        {
            return English ? en : ru;
        }
    }
}
