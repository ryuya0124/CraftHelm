# MOD設定の編集と保持（v0.1.5）

有名MOD固有の項目名を決め打ちせず、設定ファイルをそのまま保存します。新しい項目や未知の拡張子も、以下の設定フォルダ内ならプリセットへ保存されます。MOD更新による設定スキーマの変換や、MOD間の互換性判定は行いません。

## 保存する場所

| 場所 | 対象 |
|---|---|
| `mods` / `plugins` 直下 | `.jar` と `.jar.disabled` |
| `config` / `defaultconfigs` | 全ファイル。形式を解釈せずコピー |
| `<ワールド名>/serverconfig` | サーバー直下の各フォルダのserverconfigのみ。ワールド本体は除外 |
| `kubejs` / `scripts` | スクリプト、設定、付随ファイルを含む全ファイル |
| `plugins` 配下 | 編集対応拡張子のテキスト設定。DB等は新規プリセットに含めない |
| `automodpack/automodpack-server.json` | 既知のサーバー設定だけ。配布物・鍵・クライアント情報は除外 |

サーバー直下のserver.properties等は編集できますがプリセット対象外です。ワールドが`maps/example`など二階層以上の場合、そのserverconfigの自動検出は未対応です。範囲外の設定やプラグインのデータベースはサーバー全体バックアップで保管できます。

## 有名MODを扱う方針

| 種類・例 | 扱いと根拠 |
|---|---|
| Forge / NeoForge系（Create、Mekanismなど） | config、defaultconfigs、world/serverconfigのTOML等を保持。MOD・バージョンが実際に生成したパスを使う。[NeoForgeの設定仕様](https://docs.neoforged.net/docs/1.21.4/misc/config/) |
| FTB Chunksなど | SNBTの編集・保持。configとワールド別設定の両方を探索。[FTB公式設定ガイド](https://docs.feed-the-beast.com/mod-docs/mods/suite/Chunks/config/) |
| KubeJS | kubejsのスクリプト・設定・付随ファイルを保存。[公式フォルダ構成](https://kubejs.com/wiki/folder-structure) |
| CraftTweaker | scripts配下のZSを編集・保存。[公式スクリプト導入説明](https://docs.blamejared.com/1.19/en/getting_started/) |
| Lithiumなどの最適化MOD | configのproperties等を保持。[Lithium公式設定説明](https://github.com/CaffeineMC/lithium/wiki/Configuration-File) |
| spark / Chunky | 設定保持に加え実サーバーで導入・コマンド・再起動を検証。[実検証](FEATURE_VALIDATION.md) |
| AutoModpack | サーバー設定を保存。クライアントへの配布はAutoModpackの設定で別途指定。[併用ガイド](AUTOMODPACK.md) |

この表は設定ファイルを扱える範囲です。Create・Mekanism・FTB・KubeJS・CraftTweaker本体をすべて起動して検証したという意味ではありません。クライアント専用MODのサーバー導入を可能にする機能でもありません。

## 切り替え操作

1. 対象サーバーを停止し、MOD画面で「現在の構成を保存」を選びます。
2. 「現在の設定を維持」は標準でオンです。切替時は既存ファイルを残し、プリセット側にしかない設定を追加します。
3. 保存時点の設定に戻す場合はチェックを外します。同名の設定ファイルだけを上書きし、後から追加した別ファイルは残します。ファイル内のキー単位のマージは行いません。

JARの構成はどちらのモードでもプリセットに合わせます。空プリセットでも既存設定は消えません。毎回、変更前のサーバー全体をバックアップします。旧プリセットも読めますが、旧形式のpluginsに含まれるデータファイルは、チェックを外すと同名ファイルが上書きされる点に注意してください。

残したKubeJS/CraftTweakerスクリプトが削除したMODを参照する場合など、設定の維持と構成の互換性は別です。起動後のログを確認し、必要に応じて設定を調整してください。

## 編集と軽量性

対応拡張子はproperties / json / json5 / jsonc / toml / yml / yaml / txt / conf / cfg / snbt / hocon / js / zs。画面の一覧は最大500件、1ファイル1MB未満です。名前・パス検索、種類別フィルター、最近のファイルから選べます。この表示制限はプリセットのconfig等の保存範囲には適用しません。[テキスト編集の使い方](CONFIG_EDITOR.md)。

JSONのみ構文を検証します。その他はテキスト編集で、MOD側が読み込むまで構文や設定値の妥当性は分かりません。保存前の原本はfile-historyへ残し、編集結果はUTF-8で保存します。プリセット内の未編集ファイルはバイト列のままコピーします。

新しい常駐サービス・ライブラリは追加していません。ワールドのチャンク・プレイヤーデータを設定探索のために再帰走査しません。
