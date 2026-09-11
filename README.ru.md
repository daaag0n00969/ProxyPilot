# ProxyPilot

Прозрачный прокси-клиент для Windows: приложения без своих настроек прокси (Steam, лаунчеры, игры) идут через SOCKS5 / SOCKS4 / HTTP CONNECT по правилам.

Полное описание, сборка и архитектура — в [README.md](README.md).

## Коротко

1. Запустите Happ (SOCKS5 на `127.0.0.1:10808`).
2. Запустите `Запустить ProxyPilot.bat` **от администратора**.
3. Нажмите **Запустить**.

Steam и Grok Bot — через Happ. Сами Happ / xray / grok.exe — напрямую.

Профиль: `%AppData%\ProxyPilot\profile.json`  
Лог: `%AppData%\ProxyPilot\engine.log`

## Steam Cloud

Клиент резолвит `*.blob.core.windows.net` через `svchost.exe`, не через `steam.exe`. ProxyPilot отвечает на эти DNS-запросы из кэша и через DNS-over-HTTPS (порт **443**, его Happ пускает; порт 53 часто режется). TCP на Azure blob после этого идёт напрямую, если путь живой — облачные сохранения доходят до конца.
