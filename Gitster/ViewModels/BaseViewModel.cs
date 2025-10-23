using CommunityToolkit.Mvvm.ComponentModel;

namespace Gitster.ViewModels;

/// <summary>
/// Base view model that all view models inherit from.
/// Inherits from ObservableRecipient to support MVVM pattern with INPC and messaging.
/// </summary>
public abstract class BaseViewModel : ObservableRecipient
{
}
