WPF Modern File Manager
C# と WPF (Windows Presentation Foundation) で構築された、軽量でモダンなデザインのファイルマネージャーです。
非同期処理による高速なディレクトリ操作と、ダークモードを基調とした直感的なユーザーインターフェースを提供します。

🚀 主な機能
非同期ファイルブラウジング: async/await を活用し、大量のファイルがあるフォルダでも UI がフリーズしません。

リアルタイム検索: 入力と同時に表示項目を絞り込むフィルタリング機能。

スマートプレビュー:

画像ファイル（JPG, PNG, BMP等）のサムネイル表示（ファイルロック防止機能付き）。

テキストファイル（TXT, LOG, CS等）の冒頭内容プレビュー。

ファイル操作:

名前の変更: リスト上で直接編集可能（インプレイス・エディット）。

デスクトップへコピー: 選択したファイルをワンクリックで転送。

削除: フォルダを含む複数アイテムのバッチ削除。

ナビゲーション:

ブラウザのような「戻る」「進む」履歴管理。

ダブルクリックによるフォルダ移動および規定アプリでのファイル起動。

高度なソート: 名前、サイズ、更新日時による並び替え（常にフォルダを優先表示）。

🛠 技術スタック
Language: C# 10.0+ / .NET 6.0+ (or .NET Framework 4.7.2+)

Framework: WPF

Architecture: MVVM パターンの考え方を導入したコードビハインド設計

UI Assets: Segoe MDL2 Assets (Windows 標準アイコン)

📂 プロジェクト構成
MainWindow.xaml: ダークテーマを適用したレスポンシブな UI 定義。

MainWindow.xaml.cs: 非同期 I/O、イベントハンドリング、履歴管理ロジック。

FileModel: INotifyPropertyChanged を実装したデータモデル。

⚙️ セットアップと実行
Visual Studio 2022 以降を開きます。

「WPF アプリケーション」プロジェクトを作成し、名前を FileManager に設定します。

MainWindow.xaml と MainWindow.xaml.cs の内容をリポジトリのコードで上書きします。

F5 キーを押して実行します。

📝 ライセンス
このプロジェクトは MIT ライセンスの下で公開されています。商用・個人利用を問わず、自由に変更・配布が可能です。

エンジニア向けの補足（技術的特徴）
メモリ効率: 画像プレビューにおいて BitmapCacheOption.OnLoad を使用しているため、プレビュー中のファイルが Windows システムによってロックされず、表示したままリネームや削除が可能です。

仮想化: ListView の VirtualizingPanel を有効にしており、数千個のアイテムを表示してもメモリ消費を抑える設計になっています。
<img width="1228" height="737" alt="スクリーンショット 2026-05-09 105746" src="https://github.com/user-attachments/assets/bc4c2e38-85ca-475b-9c80-1ce8d9d42e25" />
