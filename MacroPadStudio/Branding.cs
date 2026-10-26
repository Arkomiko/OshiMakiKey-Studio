namespace OshiMakiKey;

/// <summary>Название программы и связанные с ним системные имена.</summary>
internal static class Branding
{
    public const string AppName = "OshiMakiKey Studio";
    public const string Version = "1.3";

    /// <summary>Имя для автозапуска и системных объектов (мьютекс одного экземпляра и т.п.).</summary>
    public const string InternalName = "OshiMakiKeyStudio";

    public const string Website = "https://github.com/Arkomiko/OshiMakiKey-Studio";

    /// <summary>Страница с новыми версиями — на неё ведёт «Проверить обновления».</summary>
    public const string ReleasesUrl = Website + "/releases";
}
