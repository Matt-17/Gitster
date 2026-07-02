using Gitster.Models;
using Gitster.ViewModels;

namespace Gitster.Tests;

[TestClass]
public sealed class DiffViewModelTests
{
    [TestMethod]
    public void SetFiles_BuildsFolderTreeAndPreservesSelectedPath()
    {
        var vm = new DiffViewModel();
        vm.SetFiles(new[]
        {
            File("src/App.xaml.cs"),
            File("docs/readme.md"),
        });
        var docsNode = vm.FileTree.Single(n => n.Name == "docs").Children.Single();

        vm.SelectNode(docsNode);
        vm.SetFiles(new[]
        {
            File("src/App.xaml.cs"),
            File("docs/readme.md", added: 2),
        });

        Assert.AreEqual("2 files", vm.FileCountText);
        Assert.AreEqual("docs/readme.md", vm.SelectedFile?.Path);
        Assert.AreEqual(2, vm.SelectedFile?.Added);
        Assert.AreEqual(1, vm.FileTree.Single(n => n.Name == "docs").FileCount);
    }

    [TestMethod]
    public void ShowAllLines_WithLargeDiff_RemovesInitialLineCap()
    {
        var lines = Enumerable.Range(0, DiffViewModel.InitialVisibleLineLimit + 5)
            .Select(i => new DiffLine(DiffLineKind.Context, $"line {i}"))
            .ToList();
        var vm = new DiffViewModel();

        vm.SetFiles(new[] { File("large.txt", lines: lines) });

        Assert.IsTrue(vm.IsSelectedDiffCapped);
        Assert.AreEqual(DiffViewModel.InitialVisibleLineLimit, vm.VisibleSelectedLines.Count);
        Assert.AreEqual("5 more lines", vm.HiddenLineText);

        vm.ShowAllLinesCommand.Execute(null);

        Assert.IsFalse(vm.IsSelectedDiffCapped);
        Assert.AreEqual(lines.Count, vm.VisibleSelectedLines.Count);
    }

    [TestMethod]
    public void SelectNode_WhenTreeViewEchoesSelectionChange_DoesNotReenterSelection()
    {
        var vm = new DiffViewModel();
        vm.SetFiles(new[]
        {
            File("a.txt"),
            File("b.txt"),
        });
        var nodes = vm.FileTree.ToList();
        var echoedChanges = 0;

        foreach (var node in nodes)
        {
            node.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(DiffTreeNode.IsSelected))
                    return;

                echoedChanges++;
                if (echoedChanges > 10)
                    Assert.Fail("Selection changes re-entered the diff tree selection pipeline.");

                vm.SelectFromTreeItem(node.IsSelected ? node : null);
            };
        }

        vm.SelectNode(nodes[1]);

        Assert.AreEqual("b.txt", vm.SelectedFile?.Path);
        Assert.IsFalse(nodes[0].IsSelected);
        Assert.IsTrue(nodes[1].IsSelected);
        Assert.IsTrue(echoedChanges <= 2);
    }

    [TestMethod]
    public void SelectFromTreeItem_WithFolderNode_ClearsSelectedFileAndTreeSelection()
    {
        var vm = new DiffViewModel();
        vm.SetFiles(new[]
        {
            File("src/App.xaml.cs"),
            File("docs/readme.md"),
        });
        var folder = vm.FileTree.Single(n => n.Name == "docs");

        folder.IsSelected = true;
        vm.SelectFromTreeItem(folder);

        Assert.IsNull(vm.SelectedFile);
        Assert.IsFalse(folder.IsSelected);
        Assert.IsFalse(vm.FileTree.Single(n => n.Name == "src").Children.Single().IsSelected);
    }

    private static DiffFileEntry File(
        string path,
        int added = 1,
        int deleted = 0,
        IReadOnlyList<DiffLine>? lines = null) =>
        new(path, added, deleted, "M", lines ?? [new DiffLine(DiffLineKind.Context, path)]);
}
