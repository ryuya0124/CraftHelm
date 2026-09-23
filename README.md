# ◈ CraftHelm（クラフトヘルム）

> **開発途中のプレビュー版です。利用は自己責任でお願いします。** 不具合による停止やデータ損失の可能性があります。重要なワールドは別の場所にもバックアップし、まずテスト用サーバーで確認してください。すべてのMOD・バージョンへの対応は保証しません。


**Minecraft Server Manager — Minecraftサーバーを管理するWindowsアプリ。** Java Editionに対応。C# / WPFで作り、ブラウザエンジンやNode.jsを同梱しません。

旧名称はCraftHarborです。既存データと自動更新の互換性のため、内部の実行ファイル名・保存先は引き継ぎます。

## ダウンロード

[GitHub Releases](https://github.com/ryuya0124/CraftHelm/releases) の **`CraftHelm-0.1.17-win-x64-setup.exe`** を実行してください。管理者権限不要、.NETランタイム同梱です。スタートメニューにCraftHelmとアンインストール項目を登録します。[インストール・更新・削除の手順](docs/INSTALLATION.md)。

ZIP版も利用できます。`portable.zip` は.NET 10 Desktop Runtime x64が必要、`standalone.zip` はランタイム同梱です。

**v0.1.17 / プレビュー**。編集欄の行間とCtrl＋ホイールの拡大・縮小を改善し、難易度・初期ゲームモードの重複した数値選択肢を整理しました。「テキスト編集」はフォルダを開閉できる階層一覧になり、ウィンドウの大きさに合わせて一覧と編集欄が広がります。設定ファイルを検索・分類して選び、JSON・TOMLなどを色分けしたエディターで編集できます。[設定ファイル編集の使い方](docs/CONFIG_EDITOR.md)。PC情報をカードと使用率バーで表示し、サーバー追加時に種類とMinecraftバージョンを選べます。更新の再起動ボタンは新しい版の検証後だけ表示し、稼働中のサーバーを停止する前に確認します。サーバー一覧のテーマ背景も修正しました。設定画面の左右を独立してスクロールできるようにし、タブを切り替えても左メニューの位置を保持します。サーバー名の右クリックメニューの白い余白も修正しました。[画面とデータの扱い](docs/UI_AND_BACKUP.md)。名称はv0.1.10で変更しました。[命名調査と移行について](docs/NAME_RESEARCH.md)。設定画面を日本語名と用途別の階層に整理し、AutoModpackなどの[MOD設定GUI](docs/JAPANESE_SETTINGS.md)を追加しました。[server.propertiesのGUI設定](docs/SERVER_PROPERTIES.md)と[GitHub Releasesによる自動更新](docs/UPDATES.md)に対応しました。起動時にローディング画面を先に表示し、バックグラウンドでサーバー一覧を読み込みます。[MOD設定の保持](docs/MOD_CONFIGURATIONS.md)。[AutoModpackとの併用](docs/AUTOMODPACK.md) / [MOD・設定の検証記録](docs/FEATURE_VALIDATION.md) / [サーバー本体の検証記録](docs/REAL_SERVER_VALIDATION.md)。すべてのMOD・パック・サーバー実装を自動的に扱える製品ではありません。

## できること

| 分野 | 対応 |
|---|---|
| 表示 | ダーク／ライト切替・保存、テーマ対応の選択欄・内部ダイアログ、専用アイコン |
| サーバー | 複数プロファイル、起動・通常停止・再起動・明示的な強制終了、選択サーバーの削除と消失フォルダの一覧反映 |
| 自動導入 | Vanilla / Paper / Fabric / Folia。バージョン選択、Fabricローダー固定 |
| 既存環境 | 停止済みサーバーフォルダのコピー。Forge / NeoForge / Quilt / 独自JARのJava起動引数 |
| Java | 既存Javaの検出、Temurin JRE 8 / 11 / 17 / 21 / 25の専用フォルダ導入・サーバー別割当 |
| コンソール | 標準出力・エラー、コマンド送信、list / save-all / whitelist list、永続ログ |
| MOD / plugins | ローカルJAR追加、有効・無効切替、フォルダ参照 |
| Modrinth | MC・ローダーで絞り込むMOD検索、必須依存解決、導入予定確認、SHA512検証 |
| MODパック | `.mrpack` の必須サーバーファイルとoverrides。Fabric指定版の引継ぎ |
| プリセット | JAR・MOD設定をZIP保存。既存設定を維持／同名設定を復元。切替前に全体バックアップ |
| サーバー設定 | server.propertiesをGUI編集。真偽値・選択肢・数値検証・秘密値マスク、未知の項目とコメントを保持 |
| アプリ更新 | 起動後・24時間ごとにGitHub Releasesを確認し、検証済み更新を通常終了後に適用 |
| MOD設定GUI | JSON・単純なTOMLの値を項目別に編集。AutoModpack・共通項目の日本語名、文字列配列、未知の内容とコメントの保持 |
| 設定 | JSON / TOML / YAML / properties / txt / confを編集、JSON構文検証、旧版保存 |
| バックアップ | 停止中の全体ZIP、進捗・キャンセル、高速／圧縮の選択、ステージング復元、復元前フォルダの保存 |
| 情報 | OS・論理CPU数・メモリ指標・ディスク空き・LAN IPv4・TCP待受・疎通 |

## 最初の起動

1. 「サーバーを追加」で名前を決めます。
2. 「種類とバージョン」でサーバーの種類を選び、「本体の導入」を開きます。
3. 「Javaの管理」で必要なJavaを導入し、選択サーバーへ割り当てます。
4. 「Java・メモリ・ポート」で設定し、Minecraft EULAを読んで同意します。
5. 「概要」から起動。「コンソール」の `Done` を確認して接続します。

既存サーバーを遊びながらCraftHelmを試せます。**別アプリから起動されたJavaプロセスには接続・停止しません。** 既存フォルダを取り込む場合だけ、元のサーバーを停止できる時間にコピーしてください。

## ドキュメント

- [操作ガイド](docs/USER_GUIDE.md)：導入、コンソール、MOD、Java、プリセット
- [運用・復旧ガイド](docs/OPERATIONS.md)：データ、バックアップ、ネットワーク、故障時
- [対応表・制約](docs/SUPPORT.md)：自動対応と手動対応の区別
- [開発ガイド](docs/DEVELOPMENT.md)：構成、ビルド、テスト、配布
- [検証記録](docs/VALIDATION.md)：実施したテストと未検証事項
- [設計](docs/ARCHITECTURE.md) / [ロードマップ](docs/ROADMAP.md) / [変更履歴](CHANGELOG.md)

## データとプライバシー

既定保存先は WindowsのDocuments配下 `CraftHarbor/data`。ソースやEXEと別に保存するため、アプリ更新でワールドが消えません。`CRAFTHARBOR_DATA` 環境変数で変更できます。アカウント登録、テレメトリー、常駐Webサーバーはありません。ダウンロード・検索時に該当配布APIへ接続します。インストーラー版では自動更新が既定で有効で、起動後・24時間ごとにGitHubへ接続します。「アプリの更新」で無効にできます。

## ビルド

Windows、.NET 10 SDKを使用します。NuGetの追加実行時依存はありません。

```powershell
dotnet build src/CraftHarbor.Desktop -c Release
dotnet run --project tests/CraftHarbor.Tests -c Release
dotnet run --project tests/CraftHarbor.UiTests -c Release
./scripts/package.ps1
```

## ライセンス

CraftHelmのコードはMIT。Minecraft、MOD、Java等のダウンロード物には各配布元のライセンス・EULAが適用されます。Mojang / Microsoftの公式製品ではありません。ServerStarter2のコード・画像を流用していません。
