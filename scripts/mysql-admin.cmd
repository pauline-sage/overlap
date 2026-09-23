@echo off
setlocal
set "MYSQL_HOME_OVERLAP=%~dp0..\.local\mysql"
if not exist "%MYSQL_HOME_OVERLAP%\overlapadmin.cnf" (
  echo MySQL account setup has not completed yet.
  exit /b 1
)
"%MYSQL_HOME_OVERLAP%\mysql-8.4.11-winx64\bin\mysql.exe" "--defaults-extra-file=%MYSQL_HOME_OVERLAP%\overlapadmin.cnf" %*
