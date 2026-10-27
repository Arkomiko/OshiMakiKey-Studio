using System.Diagnostics;
using OshiMakiKey.Model;

namespace OshiMakiKey.UI;

/// <summary>Окно «Проверить обновления и файлы»: версия из релизов GitHub и отчёт о файлах; скачивание — только по кнопке.</summary>
internal static class UpdateDialog
{
    public static async Task RunAsync(Control owner)
    {
        owner.UseWaitCursor = true;
        UpdateCheck.Release release;
        try
        {
            release = await UpdateCheck.CheckAsync();
        }
        finally
        {
            owner.UseWaitCursor = false;
        }
        if (owner.IsDisposed) return;

        var files = UpdateCheck.CheckFiles(out bool filesOk);
        string heading = release.Error != null ? L.T("updates.error", "Не удалось проверить обновления")
            : release.Latest == null ? L.T("updates.noReleases", "На GitHub пока нет опубликованных версий")
            : release.Newer ? L.F("updates.newer", "Доступна новая версия {0}", release.Latest)
            : L.F("updates.latest", "У вас последняя версия ({0})", Branding.Version);

        var lines = new List<string> { L.F("updates.current", "Установлена: {0}", Branding.Version) };
        if (release.Error != null) lines.Add(release.Error);
        if (release.Newer && !string.IsNullOrWhiteSpace(release.Notes))
        {
            string notes = release.Notes.Trim();
            lines.Add("");
            lines.Add(notes.Length > 600 ? notes[..600] + "…" : notes);
        }
        lines.Add("");
        lines.Add(L.T("updates.files", "Файлы программы:"));
        lines.AddRange(files);

        var buttons = release.Newer && release.Url != null
            ? new[] { L.T("updates.open", "Открыть страницу загрузки"), L.T("button.close", "Закрыть") }
            : new[] { L.T("button.close", "Закрыть") };
        var (button, _) = ThemedDialog.Show(owner.FindForm() ?? (IWin32Window)owner, Branding.AppName, heading, string.Join("\n", lines), buttons,
            release.Error != null || !filesOk ? DialogKind.Warning : DialogKind.Info, cancelIndex: buttons.Length - 1);
        if (button == 0 && buttons.Length == 2 && release.Url != null)
        {
            try { Process.Start(new ProcessStartInfo(release.Url) { UseShellExecute = true })?.Dispose(); }
            catch (System.ComponentModel.Win32Exception) { }
        }
    }
}
