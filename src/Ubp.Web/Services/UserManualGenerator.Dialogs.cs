using System.Text;

namespace Ubp.Web.Services;

public sealed partial class UserManualGenerator
{
    private List<ManualDialogDoc> BuildDialogs(ManualSourceSet source, string language)
    {
        var dialogs = new List<ManualDialogDoc>();
        foreach (var file in source.Pages)
        {
            if (PageRouteRegex.IsMatch(file.Text)) continue;
            var buttons = ParseButtons(file.Text, language, "");
            if (buttons.Count == 0) continue;
            var name = Path.GetFileNameWithoutExtension(file.RelativePath);
            dialogs.Add(new ManualDialogDoc(name, $"حوار {name}.", buttons));
        }
        return dialogs;
    }

    private static void AppendDialogSections(StringBuilder md, IReadOnlyList<ManualDialogDoc> dialogs)
    {
        if (dialogs.Count == 0) return;
        md.AppendLine("### الحوارات");
        md.AppendLine();
        md.AppendLine("لكل حوار: أزراره وسلوكها. الصلاحية تُورَث من الشاشة الأم إن لم تظهر على الزر.");
        md.AppendLine();
        foreach (var dialog in dialogs)
        {
            md.AppendLine($"#### {dialog.Name}");
            md.AppendLine();
            md.AppendLine(dialog.Purpose);
            md.AppendLine();
            md.AppendLine("| الزر | الصلاحية | ماذا يفعل / القيود | إلى أين |");
            md.AppendLine("|---|---|---|---|");
            foreach (var button in dialog.Buttons)
                md.AppendLine($"| {button.Label} | `{button.Permission}` | {button.Behavior} | {button.Destination} |");
            md.AppendLine();
        }
    }
}
