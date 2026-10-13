# Third-party notices

Auto Shopper ships no third-party data and no code ported from other plugins. The one bundled third-party asset is the font below; its license travels with it.

The release zip also carries the compiled libraries the plugin builds against: the ECommons submodule and the NuGet packages pinned in `AutoShopper/packages.lock.json`, with their dependencies. They ship as built and unmodified, each under its own license, and none of their source is copied into this repository.

Market prices are read at run time from the public Universalis API (universalis.app); no Universalis data ships with the plugin.

## Noto Sans

- Source: Noto Sans Medium by the Noto Project Authors, https://github.com/notofonts/latin-greek-cyrillic, shipped as `AutoShopper/Fonts/NotoSans-Medium-Latin.ttf`.
- Used: the primary font for Latin letters, digits and punctuation in the plugin's windows.
- License: SIL Open Font License 1.1, shipped next to the font in full as `AutoShopper/Fonts/OFL-NotoSans.txt`.
