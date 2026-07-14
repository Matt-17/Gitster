namespace Gitster.ApplicationLayer.Ui;

/// <summary>System clipboard access, abstracted so ViewModels stay UI-framework-free.</summary>
public interface IClipboard
{
    void SetText(string text);
}
