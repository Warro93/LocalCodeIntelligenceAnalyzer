@echo off
rem ==================================================================
rem Local Code Intelligence Analyzer - Aggiorna sorgenti da zip
rem Applica al progetto una nuova versione ricevuta come zip, poi propone build, installazione e push.
rem Esegue scripts\update-from-zip.ps1 con Windows PowerShell 5.1.
rem Non servono privilegi di amministratore. Nessun token viene richiesto.
rem ==================================================================
setlocal EnableExtensions
cd /d "%~dp0"
title LCIA - Aggiorna sorgenti da zip
rem Tutto il blocco viene letto prima di eseguirlo: lo script puo' aggiornare
rem anche questo file senza interromperne l'esecuzione.
(
  "%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\update-from-zip.ps1" %*
  if errorlevel 1 echo   Operazione non completata: leggi i messaggi qui sopra.
  echo.
  pause
  exit /b
)
