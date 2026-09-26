# PawnIO

`PawnIO_setup.exe` is the unmodified, signed official installer of PawnIO 2.2.0 by namazso
(SHA-256 `1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032`).
It is embedded in PCStats.exe and run on request when the driver is missing.

- Website: https://pawnio.eu
- Installer releases: https://github.com/namazso/PawnIO.Setup/releases
- Source code: https://github.com/namazso/PawnIO (driver), https://github.com/namazso/PawnIO.Modules (modules)
- License: GNU GPL v2.0 or later, see `COPYING`

PawnIO is a separate program; PCStats only launches its installer and talks to the installed driver
through its device IO control interface.
