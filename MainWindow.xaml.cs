using EArchive.Data;
using EArchive.Models;
using EArchive.Services;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace EArchive;

public partial class MainWindow : Window
{
    private readonly string _appRoot;
    private readonly string _archiveRoot;
    private readonly ArchiveRepository _repository;
    private readonly FileStorageService _storage;
    private readonly ExcelExportService _excel = new();
    private string? _selectedFile;
    private long? _editingRecordId;
    private ArchiveRecord? _editingRecord;
    private CancellationTokenSource? _searchCts;

    public MainWindow()
    {
        InitializeComponent();
        _appRoot = @"C:\Users\ITDEPARTMENT\Desktop\EArchive";
        _archiveRoot = Path.Combine(_appRoot, "Files");
        Directory.CreateDirectory(_appRoot);
        _repository = new ArchiveRepository(Path.Combine(_appRoot, "archive.db"));
        _storage = new FileStorageService(_archiveRoot);
        Loaded += async (_, _) => await RefreshRecordsAsync();
    }

    private void ChooseFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog {
            Title = "اختيار مستند للأرشفة",
            Filter = "المستندات المدعومة|*.pdf;*.doc;*.docx;*.xls;*.xlsx;*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.bmp|كل الملفات|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() == true) { _selectedFile = dialog.FileName; FileBox.Text = Path.GetFileName(_selectedFile); }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TitleBox.Text)) { MessageBox.Show("أدخل عنوان المستند."); return; }
        if (string.IsNullOrWhiteSpace(_selectedFile) || !File.Exists(_selectedFile)) { MessageBox.Show("اختر ملفاً أولاً."); return; }
        if (DateFromPicker.SelectedDate.HasValue && DateToPicker.SelectedDate.HasValue &&
            DateFromPicker.SelectedDate > DateToPicker.SelectedDate) { MessageBox.Show("تاريخ البداية يجب ألا يتجاوز تاريخ النهاية."); return; }

        SetBusy(true, "جارٍ حفظ الملف وتسجيل البيانات...");
        string? storedPath = null;
        try
        {
            var stored = await _storage.StoreAsync(_selectedFile);
            storedPath = stored.relativePath;
            var record = new ArchiveRecord {
                Title = TitleBox.Text.Trim(), Source = SourceBox.Text.Trim(),
                Destination = DestinationBox.Text.Trim(), Notes = NotesBox.Text.Trim(),
                DateFrom = DateFromPicker.SelectedDate, DateTo = DateToPicker.SelectedDate,
                OriginalFileName = Path.GetFileName(_selectedFile), StoredRelativePath = stored.relativePath,
                Extension = stored.extension, FileSize = stored.size, CreatedAt = DateTime.Now
            };
            await _repository.AddAsync(record);
            _selectedFile = null; FileBox.Clear(); TitleBox.Clear(); SourceBox.Clear(); DestinationBox.Clear(); NotesBox.Clear();
            DateFromPicker.SelectedDate = null; DateToPicker.SelectedDate = null;
            await RefreshRecordsAsync();
            StatusText.Text = "تم حفظ المستند بنجاح.";
        }
        catch (Exception ex)
        {
            // الملف قد يكون نُسخ بالفعل قبل فشل إدخال قاعدة البيانات؛ لا نحذفه تلقائياً دون سياسة استرداد.
            MessageBox.Show($"تعذر حفظ المستند:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "فشل الحفظ.";
        }
        finally { SetBusy(false, StatusText.Text); }
    }


    private void RecordsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RecordsGrid.SelectedItem is not ArchiveRecord record) return;
        _editingRecord = record;
        _editingRecordId = record.Id;
        TitleBox.Text = record.Title;
        SourceBox.Text = record.Source;
        DestinationBox.Text = record.Destination;
        NotesBox.Text = record.Notes;
        DateFromPicker.SelectedDate = record.DateFrom;
        DateToPicker.SelectedDate = record.DateTo;
        _selectedFile = null; // إن لم يتم اختيار مرفق جديد، يحتفظ التعديل بالمرفق الحالي.
        FileBox.Text = record.OriginalFileName;
        StatusText.Text = $"تم تحميل السجل رقم {record.Id} للتعديل. اضغط «تعديل السجل المحدد» لحفظ التغييرات.";
    }

    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (RecordsGrid.SelectedItem is not ArchiveRecord selected)
        {
            MessageBox.Show("حدد سجلاً من الجدول أولاً.");
            return;
        }
        if (string.IsNullOrWhiteSpace(TitleBox.Text))
        {
            MessageBox.Show("أدخل عنوان المستند.");
            return;
        }
        if (DateFromPicker.SelectedDate.HasValue && DateToPicker.SelectedDate.HasValue &&
            DateFromPicker.SelectedDate > DateToPicker.SelectedDate)
        {
            MessageBox.Show("تاريخ البداية يجب ألا يتجاوز تاريخ النهاية.");
            return;
        }

        try
        {
            SetBusy(true, "جارٍ تعديل السجل...");
            var record = new ArchiveRecord
            {
                Id = selected.Id,
                Title = TitleBox.Text.Trim(),
                Source = SourceBox.Text.Trim(),
                Destination = DestinationBox.Text.Trim(),
                Notes = NotesBox.Text.Trim(),
                DateFrom = DateFromPicker.SelectedDate,
                DateTo = DateToPicker.SelectedDate,
                OriginalFileName = selected.OriginalFileName,
                StoredRelativePath = selected.StoredRelativePath,
                Extension = selected.Extension,
                FileSize = selected.FileSize,
                CreatedAt = selected.CreatedAt
            };

            if (!string.IsNullOrWhiteSpace(_selectedFile) && File.Exists(_selectedFile))
            {
                var stored = await _storage.StoreAsync(_selectedFile);
                record.OriginalFileName = Path.GetFileName(_selectedFile);
                record.StoredRelativePath = stored.relativePath;
                record.Extension = stored.extension;
                record.FileSize = stored.size;
            }

            await _repository.UpdateAsync(record);
            if (!string.Equals(record.StoredRelativePath, selected.StoredRelativePath, StringComparison.OrdinalIgnoreCase))
            {
                var oldPath = _storage.GetFullPath(selected.StoredRelativePath);
                if (File.Exists(oldPath)) File.Delete(oldPath);
            }
            _selectedFile = null;
            _editingRecord = null;
            _editingRecordId = null;
            FileBox.Clear();
            TitleBox.Clear(); SourceBox.Clear(); DestinationBox.Clear(); NotesBox.Clear();
            DateFromPicker.SelectedDate = null; DateToPicker.SelectedDate = null;
            await RefreshRecordsAsync();
            StatusText.Text = "تم تعديل السجل بنجاح.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"تعذر تعديل السجل:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "فشل تعديل السجل.";
        }
        finally { SetBusy(false, StatusText.Text); }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (RecordsGrid.SelectedItem is not ArchiveRecord record)
        {
            MessageBox.Show("حدد سجلاً من الجدول أولاً.");
            return;
        }

        var answer = MessageBox.Show(
            $"هل تريد حذف السجل رقم {record.Id} بعنوان «{record.Title}»؟\nسيتم حذف السجل وملفه المؤرشف.",
            "تأكيد الحذف", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes) return;

        try
        {
            SetBusy(true, "جارٍ حذف السجل...");
            var path = _storage.GetFullPath(record.StoredRelativePath);
            await _repository.DeleteAsync(record.Id);
            // حذف الملف بعد نجاح حذف سجل قاعدة البيانات.
            if (File.Exists(path)) File.Delete(path);

            _selectedFile = null;
            _editingRecord = null;
            _editingRecordId = null;
            FileBox.Clear();
            TitleBox.Clear(); SourceBox.Clear(); DestinationBox.Clear(); NotesBox.Clear();
            DateFromPicker.SelectedDate = null; DateToPicker.SelectedDate = null;
            await RefreshRecordsAsync();
            StatusText.Text = "تم حذف السجل وملفه بنجاح.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"تعذر حذف السجل أو الملف:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "حدث خطأ أثناء الحذف.";
        }
        finally { SetBusy(false, StatusText.Text); }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshRecordsAsync();
    private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => await RefreshRecordsAsync();
    private async void FilterChanged(object sender, SelectionChangedEventArgs e) => await RefreshRecordsAsync();
    private async void ClearFilters_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Clear(); FilterFromPicker.SelectedDate = null; FilterToPicker.SelectedDate = null;
        await RefreshRecordsAsync();
    }

    private async Task RefreshRecordsAsync()
    {
        _searchCts?.Cancel(); _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;
        var query = SearchBox?.Text ?? "";
        var from = FilterFromPicker?.SelectedDate;
        var to = FilterToPicker?.SelectedDate;
        if (from.HasValue && to.HasValue && from > to) { StatusText.Text = "نطاق التاريخ غير صحيح."; return; }
        try
        {
            // مهلة بسيطة لتقليل عدد استعلامات البحث أثناء الكتابة المتتابعة.
            await Task.Delay(180, token);
            var records = await _repository.SearchAsync(query, from, to, token);
            if (!token.IsCancellationRequested) RecordsGrid.ItemsSource = records;
            StatusText.Text = $"عدد السجلات المعروضة: {records.Count}";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { StatusText.Text = $"خطأ في البحث: {ex.Message}"; }
    }

    private void OpenSelected_Click(object sender, RoutedEventArgs e)
    {
        if (RecordsGrid.SelectedItem is not ArchiveRecord record) { MessageBox.Show("حدد سجلاً أولاً."); return; }
        try
        {
            var path = _storage.GetFullPath(record.StoredRelativePath);
            if (!File.Exists(path)) { MessageBox.Show("الملف غير موجود في مجلد الأرشيف."); return; }
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show($"تعذر فتح الملف: {ex.Message}"); }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "Excel Workbook|*.xlsx", FileName = $"Archive_{DateTime.Now:yyyyMMdd_HHmm}.xlsx" };
        if (dialog.ShowDialog() != true) return;
        SetBusy(true, "جارٍ إنشاء تقرير Excel...");
        try
        {
            var records = await _repository.GetAllAsync();
            await Task.Run(() => _excel.ExportAsync(records, dialog.FileName));
            StatusText.Text = $"تم تصدير {records.Count} سجل إلى Excel.";
            MessageBox.Show("تم إنشاء ملف Excel بنجاح.");
        }
        catch (Exception ex) { MessageBox.Show($"فشل التصدير: {ex.Message}"); }
        finally { SetBusy(false, StatusText.Text); }
    }

    private void SetBusy(bool busy, string message)
    {
        IsEnabled = !busy;
        StatusText.Text = message;
        Cursor = busy ? System.Windows.Input.Cursors.Wait : System.Windows.Input.Cursors.Arrow;
    }
}