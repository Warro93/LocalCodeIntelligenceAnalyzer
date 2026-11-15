@echo off
rem ==================================================================
rem Local Code Intelligence Analyzer - Installa o aggiorna
rem Compila, testa e installa o aggiorna l app per il tuo utente: menu Start, desktop, comando lcia.
rem Esegue scripts\install-app.ps1 con Windows PowerShell 5.1.
rem Non servono privilegi di amministratore. Nessun token viene richiesto.
rem ==================================================================
setlocal EnableExtensions
cd /d "%~dp0"
title LCIA - Installa o aggiorna
rem Tutto il blocco viene letto prima di eseguirlo: lo script puo' aggiornare
rem anche questo file senza interromperne l'esecuzione.
(
  "%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\install-app.ps1" %*
  if errorlevel 1 echo   Operazione non completata: leggi i messaggi qui sopra.
  echo.
  pause
  exit /b
)
