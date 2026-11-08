@echo off
setlocal
py -3 -B "%~dp0tools\local_game.py" play
if errorlevel 1 pause
