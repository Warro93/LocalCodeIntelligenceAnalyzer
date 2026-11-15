@echo off
rem ==================================================================
rem Local Code Intelligence Analyzer - GitHub - invia modifiche
rem Controlla le modifiche, crea un commit firmato e lo invia su GitHub, con conferma a ogni passo.
rem Esegue scripts\03-review-and-push.ps1 con Windows PowerShell 5.1.
rem Non servono privilegi di amministratore. Nessun token viene richiesto.
rem ==================================================================
setlocal EnableExtensions
cd /d "%~dp0"
title LCIA - GitHub - invia modifiche
rem Tutto il blocco viene letto prima di eseguirlo: lo script puo' aggiornare
rem anche questo file senza interromperne l'esecuzione.
(
  "%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\03-review-and-push.ps1" %*
  if errorlevel 1 echo   Operazione non completata: leggi i messaggi qui sopra.
  echo.
  pause
  exit /b
)
