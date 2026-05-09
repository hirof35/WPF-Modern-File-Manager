using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace FileManager
{
    public partial class MainWindow : Window
    {
        // ObservableCollection を使うことで View への反映を自動化
        private ObservableCollection<FileModel> _files = new ObservableCollection<FileModel>();
        private Stack<string> _backHistory = new Stack<string>();
        private Stack<string> _forwardHistory = new Stack<string>();
        private string _currentPath = string.Empty;
        private ListSortDirection _lastDirection = ListSortDirection.Ascending;

        public MainWindow()
        {
            InitializeComponent();
            FileListView.ItemsSource = _files;

            // 初期パス。アクセス権限を考慮しデスクトップ等に変更推奨
            string initialPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            _ = LoadDirectoryAsync(initialPath);
        }

        // --- データ読み込み (非同期化) ---
        private async Task LoadDirectoryAsync(string path, bool isNavigation = false)
        {
            if (!Directory.Exists(path)) return;

            try
            {
                // UIスレッドをブロックせずにファイル一覧を取得
                var dirInfo = new DirectoryInfo(path);
                var items = await Task.Run(() =>
                {
                    var list = new List<FileModel>();
                    foreach (var dir in dirInfo.EnumerateDirectories())
                        list.Add(new FileModel(dir.Name, dir.FullName, "フォルダ", dir.LastWriteTime, 0));

                    foreach (var file in dirInfo.EnumerateFiles())
                        list.Add(new FileModel(file.Name, file.FullName, "ファイル", file.LastWriteTime, file.Length));

                    return list;
                });

                // 履歴管理
                if (!isNavigation && !string.IsNullOrEmpty(_currentPath) && _currentPath != path)
                {
                    _backHistory.Push(_currentPath);
                    _forwardHistory.Clear();
                }

                _files.Clear();
                foreach (var item in items) _files.Add(item);

                _currentPath = path;
                PathTextBox.Text = path;
                UpdateNavButtons();
                ApplyDefaultSort();
            }
            catch (UnauthorizedAccessException) { MessageBox.Show("アクセス権限がありません。"); }
            catch (Exception ex) { MessageBox.Show($"エラー: {ex.Message}"); }
        }

        private void ApplyDefaultSort()
        {
            ICollectionView view = CollectionViewSource.GetDefaultView(_files);
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new SortDescription(nameof(FileModel.IsDirectory), ListSortDirection.Descending));
            view.SortDescriptions.Add(new SortDescription(nameof(FileModel.Name), ListSortDirection.Ascending));
        }

        // --- プレビュー (ファイルロック対策済み) ---
        private async void FileListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FileListView.SelectedItem is FileModel selected)
            {
                PreviewName.Text = selected.Name;
                PreviewImage.Source = null;
                PreviewText.Text = "読み込み中...";

                try
                {
                    string ext = Path.GetExtension(selected.FullPath).ToLower();
                    if (new[] { ".jpg", ".jpeg", ".png", ".bmp" }.Contains(ext))
                    {
                        // ファイルをロックせずに画像を読み込む
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(selected.FullPath);
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();

                        PreviewImage.Source = bitmap;
                        PreviewImage.Visibility = Visibility.Visible;
                        PreviewText.Visibility = Visibility.Collapsed;
                    }
                    else if (new[] { ".txt", ".log", ".cs", ".json" }.Contains(ext))
                    {
                        // 大きなファイル対策として最初の2KBだけ非同期で読む
                        using (var reader = new StreamReader(selected.FullPath))
                        {
                            char[] buffer = new char[2048];
                            int read = await reader.ReadAsync(buffer, 0, buffer.Length);
                            PreviewText.Text = new string(buffer, 0, read);
                        }
                        PreviewImage.Visibility = Visibility.Collapsed;
                        PreviewText.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        PreviewText.Text = "プレビュー不可";
                    }
                }
                catch { PreviewText.Text = "プレビューを取得できませんでした。"; }
            }
        }
        private void CopyFileWithProgress(string source, string dest, IProgress<double> progress)
        {
            // ファイルを開く
            using (var src = File.OpenRead(source))
            using (var dst = File.Create(dest))
            {
                byte[] buffer = new byte[81920]; // 80KBバッファ
                long totalBytes = src.Length;
                long totalRead = 0;
                int read;

                // 読み込みと書き込みをループ
                while ((read = src.Read(buffer, 0, buffer.Length)) > 0)
                {
                    dst.Write(buffer, 0, read);
                    totalRead += read;

                    // 進捗を報告 (0.0 ～ 100.0)
                    if (totalBytes > 0)
                    {
                        double percentage = (double)totalRead / totalBytes * 100;
                        progress.Report(percentage);
                    }
                }
            }
        }
        // --- ファイルコピー (キャンセル・エラー処理対応) ---
        private async void CopyToDesktop_Click(object sender, RoutedEventArgs e)
        {
            if (FileListView.SelectedItem is FileModel selected && !selected.IsDirectory)
            {
                string dest = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), selected.Name);
                if (File.Exists(dest))
                {
                    if (MessageBox.Show("上書きしますか？", "確認", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                }

                ProgressPanel.Visibility = Visibility.Visible;
                var progress = new Progress<double>(v => CopyProgressBar.Value = v);

                try
                {
                    await Task.Run(() => CopyFileWithProgress(selected.FullPath, dest, progress));
                    MessageBox.Show("コピー完了");
                }
                catch (Exception ex) { MessageBox.Show($"コピー失敗: {ex.Message}"); }
                finally { ProgressPanel.Visibility = Visibility.Collapsed; }
            }
        }
        private async void DeleteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            // 選択されているアイテムをリストで取得（複数選択対応）
            var selectedItems = FileListView.SelectedItems.Cast<FileModel>().ToList();

            if (selectedItems.Count == 0) return;

            // 確認メッセージ
            string message = selectedItems.Count == 1
                ? $"'{selectedItems[0].Name}' を削除しますか？"
                : $"{selectedItems.Count} 個の項目を削除しますか？";

            var result = MessageBox.Show(message, "削除の確認", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    await Task.Run(() =>
                    {
                        foreach (var item in selectedItems)
                        {
                            if (item.IsDirectory)
                                Directory.Delete(item.FullPath, true); // フォルダ内も削除
                            else
                                File.Delete(item.FullPath);
                        }
                    });

                    // 削除が終わったら表示を更新
                    await LoadDirectoryAsync(_currentPath, true);
                    MessageBox.Show("削除が完了しました。");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"削除中にエラーが発生しました: {ex.Message}");
                }
            }
        }
        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (_backHistory.Count > 0)
            {
                _forwardHistory.Push(_currentPath);
                _ = LoadDirectoryAsync(_backHistory.Pop(), true);
            }
        }

        private void UpdateNavButtons()
        {
            BackButton.IsEnabled = _backHistory.Count > 0;
            ForwardButton.IsEnabled = _forwardHistory.Count > 0;
        }

        // 検索フィルター用
        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string query = SearchTextBox.Text.ToLower();
            ICollectionView view = CollectionViewSource.GetDefaultView(_files);
            view.Filter = obj =>
            {
                if (obj is FileModel f) return f.Name.ToLower().Contains(query);
                return false;
            };
        }
        private async void ForwardButton_Click(object sender, RoutedEventArgs e)
        {
            if (_forwardHistory.Count > 0)
            {
                // 現在のパスを「戻る」履歴へ
                _backHistory.Push(_currentPath);

                // 「進む」履歴から取り出して移動
                string nextPath = _forwardHistory.Pop();
                await LoadDirectoryAsync(nextPath, true);
            }
        }
        private async void FileListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // ダブルクリックされたアイテムが FileModel かどうか確認
            if (FileListView.SelectedItem is FileModel selected)
            {
                if (selected.IsDirectory)
                {
                    // フォルダなら、そのパスを読み込む
                    await LoadDirectoryAsync(selected.FullPath);
                }
                else
                {
                    // ファイルなら、Windowsの規定のアプリで開く
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = selected.FullPath,
                            UseShellExecute = true // これを true にしないとアプリが開けません
                        });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"ファイルを開けませんでした: {ex.Message}");
                    }
                }
            }
        }
        private void RenameMenuItem_Click(object sender, RoutedEventArgs e)
        {
            // ListViewで現在選択されているアイテムを取得
            if (FileListView.SelectedItem is FileModel selected)
            {
                // 編集フラグを立てる（これによりXAMLのDataTriggerが反応し、TextBoxが表示される）
                selected.IsEditing = true;

                // UIを強制的に再描画して変更を反映させる
                FileListView.Items.Refresh();
            }
        }
        private async void RenameTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                var textBox = (TextBox)sender;
                var item = (FileModel)textBox.DataContext;

                try
                {
                    // 現在のフォルダパスを取得し、新しい名前と結合
                    string directory = Path.GetDirectoryName(item.FullPath);
                    string newPath = Path.Combine(directory, textBox.Text);

                    if (item.FullPath != newPath)
                    {
                        // ファイルまたはフォルダの名前を変更（移動）
                        if (item.IsDirectory)
                            Directory.Move(item.FullPath, newPath);
                        else
                            File.Move(item.FullPath, newPath);
                    }

                    // 編集モードを終了して一覧を更新
                    item.IsEditing = false;
                    await LoadDirectoryAsync(_currentPath, true);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"名前の変更に失敗しました: {ex.Message}");
                    item.IsEditing = false;
                    FileListView.Items.Refresh();
                }
            }
            else if (e.Key == Key.Escape)
            {
                // Escキーが押されたら変更を破棄して編集モードを抜ける
                if (FileListView.SelectedItem is FileModel item)
                {
                    item.IsEditing = false;
                    FileListView.Items.Refresh();
                }
            }
        }
        private void RenameTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            // 現在選択されているアイテムがあれば、編集モード(IsEditing)を終了させる
            if (FileListView.SelectedItem is FileModel item)
            {
                item.IsEditing = false;

                // UIに編集終了（テキスト表示への切り替え）を通知
                FileListView.Items.Refresh();
            }
        }
        private void SortHeader_Click(object sender, RoutedEventArgs e)
        {
            var header = sender as GridViewColumnHeader;
            if (header == null || header.Tag == null) return;

            string sortBy = header.Tag.ToString();
            ICollectionView view = CollectionViewSource.GetDefaultView(_files);

            // 現在のソート方向を確認し、クリックごとに反転させる
            ListSortDirection direction = ListSortDirection.Ascending;
            if (view.SortDescriptions.Count > 0 && view.SortDescriptions[0].PropertyName == sortBy)
            {
                direction = view.SortDescriptions[0].Direction == ListSortDirection.Ascending
                            ? ListSortDirection.Descending
                            : ListSortDirection.Ascending;
            }

            view.SortDescriptions.Clear();

            // 常に「フォルダを上」に表示しつつ、選択した項目でソート
            view.SortDescriptions.Add(new SortDescription(nameof(FileModel.IsDirectory), ListSortDirection.Descending));
            view.SortDescriptions.Add(new SortDescription(sortBy, direction));
        }
        // --- データモデル (通知機能追加) ---
        public class FileModel : INotifyPropertyChanged
        {
            private bool _isEditing;
            public string Name { get; set; }
            public string FullPath { get; set; }
            public string Type { get; set; }
            public DateTime LastModified { get; set; }
            public long SizeBytes { get; set; }

            public bool IsEditing
            {
                get => _isEditing;
                set { _isEditing = value; OnPropertyChanged(nameof(IsEditing)); }
            }

            public bool IsDirectory => Type == "フォルダ";
            public string Icon => IsDirectory ? "📁" : "📄";
            public string DisplaySize => IsDirectory ? "" : GetFriendlySize(SizeBytes);

            public FileModel(string n, string p, string t, DateTime d, long s)
            {
                Name = n; FullPath = p; Type = t; LastModified = d; SizeBytes = s;
            }

            private string GetFriendlySize(long bytes)
            {
                string[] suf = { "B", "KB", "MB", "GB", "TB" };
                if (bytes == 0) return "0 B";
                long absBytes = Math.Abs(bytes);
                int place = Convert.ToInt32(Math.Floor(Math.Log(absBytes, 1024)));
                double num = Math.Round(absBytes / Math.Pow(1024, place), 1);
                return (Math.Sign(bytes) * num).ToString() + " " + suf[place];
            }

            public event PropertyChangedEventHandler PropertyChanged;
            protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
