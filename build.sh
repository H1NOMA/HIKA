#!/usr/bin/env bash
#
# Полная проверка без Windows.
#
# Разработка HIKA идёт не на Windows, но это не повод отправлять человеку
# непроверенный код: тесты разбора команд запускаются где угодно, а само
# приложение под Windows отсюда как минимум компилируется.
#
#   ./build.sh          тесты, затем сборка под Windows
#   ./build.sh test     только тесты (полсекунды)
#   ./build.sh app      только сборка под Windows
#
set -euo pipefail

cd "$(dirname "$0")"

DOTNET="${DOTNET:-dotnet}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

target="${1:-all}"

run_tests() {
  echo "── Тесты (net9.0, без Windows) ─────────────────────────────────"
  "$DOTNET" test tests/Hika.Tests/Hika.Tests.csproj -c Release --verbosity quiet
}

build_app() {
  echo
  echo "── Сборка под Windows ──────────────────────────────────────────"
  # EnableWindowsTargeting подтягивает пакеты Windows Desktop из NuGet,
  # благодаря чему Windows-приложение компилируется и на Linux.
  # Запустить его здесь, разумеется, нельзя — только собрать.
  "$DOTNET" build src/Hika.App/Hika.App.csproj -c Release -p:EnableWindowsTargeting=true
}

case "$target" in
  test) run_tests ;;
  app)  build_app ;;
  all)  run_tests; build_app ;;
  *)    echo "Неизвестная цель: $target (ожидается test, app или all)"; exit 1 ;;
esac

echo
echo "Готово."
