# TradeAssistant

Source code for https://mod.io/g/eco/m/trade-assistant

## Localization

User-facing strings use Eco's `Localizer` API and can be translated with gettext-style `.po` files placed in the `Locales` folder of the installed mod:

```
Mods/TradeAssistant/Locales/<language>.po
```

`publish.ps1` copies the `Locales` folder next to the mod DLL automatically. Currently available languages: English (`en`), Spanish (`es`), German (`de`), French (`fr`), Portuguese (`pt`) and Simplified Chinese (`zh`).

To add a language, copy `Locales/en.po` to `Locales/<lang>.po`, translate each `msgstr` and keep the `msgid` values unchanged. Unfinished entries keep their `msgstr` empty so Eco falls back to the English source string.
