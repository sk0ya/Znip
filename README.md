# Znip

BeefText 風のスニペット(定型文)ツール。WPF 製、外部パッケージ依存なし。

## 機能

- **ピッカー** — `Ctrl + Shift + Space`(変更可)でどこからでも呼び出し。
  インクリメンタル検索 → `Enter` でアクティブなアプリにそのまま貼り付け。
  `Ctrl + Enter` でクリップボードへのコピーのみ。`Esc` で閉じる。
- **自動展開(BeefText 方式)** — キーワード(例: `;date`)を打ち終わった瞬間に、
  どのアプリでも本文へ自動で置き換え。設定でオン/オフ可能。
- **設定画面** — スニペットの追加・編集・削除。変更はすべて自動保存。
  ホットキーの変更、スタートアップ登録もここから。
- **トレイ常駐** — ウィンドウを閉じてもトレイに常駐。ダブルクリックで設定画面。
- **変数** — 本文中で使用可能:

  | 変数 | 展開結果 |
  |---|---|
  | `{date}` | 今日の日付 (yyyy/MM/dd) |
  | `{date:yyyy年M月d日}` | 書式指定 (.NET の日付書式) |
  | `{time}` | 現在時刻 (HH:mm) |
  | `{clipboard}` | クリップボードの内容 |
  | `{cursor}` | 貼り付け後にカーソルを置く位置 |

## ビルドと実行

```
dotnet build
dotnet run
```

要件: .NET 9 SDK / Windows

## データの保存場所

`%APPDATA%\Znip\snippets.json` / `settings.json`(手動編集・バックアップ可)

## 既知の制限

- 自動展開は半角の直接入力が対象です。IME で変換中の文字列からは展開されません
  (ピッカー経由の貼り付けは IME の影響を受けません)。
- 誤爆防止のため、キーワードの先頭には `;` などの記号を付けるのがおすすめです。
- 貼り付けはクリップボード経由(Ctrl+V)です。元のクリップボードのテキストは
  貼り付け後に復元されますが、画像などテキスト以外の内容は復元されません。

## 構成

```
Models/    Snippet, AppSettings
Services/  SnippetStore(JSON 永続化・自動保存) / HotkeyManager(グローバルホットキー)
           KeyboardHook(自動展開用 低レベルフック) / TextInjector(クリップボード貼り付け)
           TemplateEngine(変数展開) / StartupManager(自動起動)
Views/     MainWindow(設定画面) / PickerWindow(ピッカー)
```
