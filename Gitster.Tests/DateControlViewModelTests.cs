using System.ComponentModel;
using Gitster.Controls;
using Gitster.ViewModels;

namespace Gitster.Tests;

[TestClass]
public sealed class DateControlViewModelTests
{
    [TestMethod]
    public void DateText_WhileFocusedWithValidPartial_KeepsTypedTextAndUpdatesSelectedDate()
    {
        var vm = new DateControlViewModel
        {
            EditMode = EditMode.DateTime,
            SelectedDate = new DateTime(2026, 6, 30, 14, 5, 0)
        };

        vm.BeginDateTextEdit();
        vm.DateText = "30.1.2026";

        Assert.AreEqual(new DateTime(2026, 1, 30, 14, 5, 0), vm.SelectedDate);
        Assert.AreEqual("30.1.2026", vm.DateText);
        Assert.AreEqual(string.Empty, ErrorFor(vm, nameof(DateControlViewModel.DateText)));

        vm.EndDateTextEdit();

        Assert.AreEqual("30.01.2026", vm.DateText);
        Assert.AreEqual("14:05", vm.TimeText);
    }

    [TestMethod]
    public void DateText_WhileFocusedWithInvalidText_KeepsTypedTextAndValidationError()
    {
        var originalDate = new DateTime(2026, 6, 30, 14, 5, 0);
        var vm = new DateControlViewModel
        {
            EditMode = EditMode.DateTime,
            SelectedDate = originalDate
        };

        vm.BeginDateTextEdit();
        vm.DateText = "30..2026";

        Assert.AreEqual(originalDate, vm.SelectedDate);
        Assert.AreEqual("30..2026", vm.DateText);
        Assert.AreNotEqual(string.Empty, ErrorFor(vm, nameof(DateControlViewModel.DateText)));

        vm.EndDateTextEdit();

        Assert.AreEqual("30.06.2026", vm.DateText);
        Assert.AreEqual(string.Empty, ErrorFor(vm, nameof(DateControlViewModel.DateText)));
    }

    [TestMethod]
    public void TimeText_WhileFocusedWithValidPartial_KeepsTypedTextAndUpdatesSelectedDate()
    {
        var vm = new DateControlViewModel
        {
            EditMode = EditMode.DateTime,
            SelectedDate = new DateTime(2026, 6, 30, 14, 30, 0)
        };

        vm.BeginTimeTextEdit();
        vm.TimeText = "9:05";

        Assert.AreEqual(new DateTime(2026, 6, 30, 9, 5, 0), vm.SelectedDate);
        Assert.AreEqual("9:05", vm.TimeText);
        Assert.AreEqual(string.Empty, ErrorFor(vm, nameof(DateControlViewModel.TimeText)));

        vm.EndTimeTextEdit();

        Assert.AreEqual("09:05", vm.TimeText);
    }

    [DataTestMethod]
    [DataRow("20260630")]
    [DataRow("2026-06-30")]
    [DataRow("26-06-30")]
    public void DateText_WithYearFirstDeveloperFormats_ParsesAsYearMonthDay(string text)
    {
        var vm = new DateControlViewModel
        {
            EditMode = EditMode.DateTime,
            SelectedDate = new DateTime(2026, 1, 1, 14, 5, 0)
        };

        vm.BeginDateTextEdit();
        vm.DateText = text;

        Assert.AreEqual(new DateTime(2026, 6, 30, 14, 5, 0), vm.SelectedDate);
        Assert.AreEqual(string.Empty, ErrorFor(vm, nameof(DateControlViewModel.DateText)));
    }

    [TestMethod]
    public void DateText_WithDottedShortDate_ParsesAsDayMonthYear()
    {
        var vm = new DateControlViewModel
        {
            EditMode = EditMode.DateTime,
            SelectedDate = new DateTime(2026, 1, 1, 14, 5, 0)
        };

        vm.BeginDateTextEdit();
        vm.DateText = "30.06.26";

        Assert.AreEqual(new DateTime(2026, 6, 30, 14, 5, 0), vm.SelectedDate);
        Assert.AreEqual(string.Empty, ErrorFor(vm, nameof(DateControlViewModel.DateText)));
    }

    private static string ErrorFor(DateControlViewModel vm, string propertyName)
    {
        return ((IDataErrorInfo)vm)[propertyName];
    }
}
