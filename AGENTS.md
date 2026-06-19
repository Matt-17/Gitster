# Repository Guidelines

## Project Structure & Module Organization

Gitster is a Windows WPF application targeting .NET 9. The main app lives in `Gitster/`; entry points are `App.xaml` and `MainWindow.xaml`. Keep UI markup in `Views/`, reusable controls in `Controls/`, view models in `ViewModels/`, domain models in `Models/`, converters in `Converters/`, and Git/runtime logic in `Services/`. Git backends are under `Services/Git/`. Shared styling is in `Themes/`, and image/icon assets are in `Assets/`.

Tests live in `Gitster.Tests/`. `RepoTest/` is a large checked-in fixture repository used for exercising repository scenarios; avoid broad edits there unless the test fixture itself is the target.

## Build, Test, and Development Commands

- `dotnet restore Gitster.sln` restores NuGet packages.
- `dotnet build Gitster.sln` builds the app and test project.
- `dotnet run --project Gitster/Gitster.csproj` launches the WPF app on Windows.
- `dotnet test Gitster.Tests/Gitster.Tests.csproj` runs the MSTest suite.

Some integration tests require the `git` CLI. Tests that cannot find Git should mark themselves inconclusive rather than failing for missing host tooling.

## Coding Style & Naming Conventions

Use C# with nullable references enabled, implicit usings, and file-scoped namespaces. Follow the existing 4-space indentation in C# files. Prefer CommunityToolkit.Mvvm patterns such as `[ObservableProperty]`, `[RelayCommand]`, and `ObservableRecipient` over manual property-notification boilerplate.

Name view models with a `ViewModel` suffix, WPF controls with matching `.xaml` and `.xaml.cs` files, and services with clear responsibility names such as `RecentReposService` or `GitCliBackend`. Keep code-behind small; put behavior in view models or services unless it is UI plumbing.

## Testing Guidelines

Use MSTest with `[TestClass]` and `[TestMethod]`. Test method names should describe behavior in the existing `Action_Condition_Result` style, for example `Fixup_OnConflict_AbortsAndRestoresPreOpStateWithChangesStaged`. Use `GitTestRepo` for isolated on-disk Git repositories instead of relying on developer-local repositories.

## Commit & Pull Request Guidelines

History uses short, descriptive subjects, often with phase or area prefixes such as `A9/A12/A13/A14:` or `Part B (Phase 4):`. Keep commits focused and explain user-visible behavior or risk in the body when needed.

Pull requests should include a concise summary, test results (`dotnet test ...`), linked issues or plan items when applicable, and screenshots or short recordings for visible WPF UI changes.

## Security & Configuration Tips

Do not commit machine-local Visual Studio state, credentials, or generated temporary repositories. Keep Git operations deterministic in tests by setting repository-local config, as `GitTestRepo` does.
