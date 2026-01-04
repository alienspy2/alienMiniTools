@echo off
echo Attempting to open Firewall ports for MiniTCPTunnel...
echo This script must be run as Administrator.
echo.

netsh advfirewall firewall add rule name="MiniTCPTunnel Control" dir=in action=allow protocol=TCP localport=9000
netsh advfirewall firewall add rule name="MiniTCPTunnel Data" dir=in action=allow protocol=TCP localport=8080-8090

echo.
echo Check Rule Added:
netsh advfirewall firewall show rule name="MiniTCPTunnel Control"
netsh advfirewall firewall show rule name="MiniTCPTunnel Data"

echo.
echo Done. If you see "Ok.", rules are added.
pause
