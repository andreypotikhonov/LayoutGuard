# Security and privacy

LayoutGuard processes keyboard events in memory so it can replace text in the active application. It does not contain telemetry, network requests, logging of typed text, or cloud services.

Processing is paused when Windows UI Automation reports a password field and for the password-manager processes listed in the local settings file. Because applications expose accessibility metadata differently, users should still disable LayoutGuard from its tray menu before entering unusually sensitive text in an application that uses a custom password control.

Please report vulnerabilities privately through GitHub Security Advisories rather than a public issue.
