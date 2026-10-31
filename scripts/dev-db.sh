#!/usr/bin/env bash
# تجهيز قاعدة تطوير محلية بلا .env — Linux/macOS
#   bash scripts/dev-db.sh
set -euo pipefail

PORT="${PORT:-5432}"
PASSWORD="${PASSWORD:-postgres}"
CONTAINER="${CONTAINER:-ubp-postgres}"
DB="${DB:-ubp}"
QA_DB="${QA_DB:-ubp_qa}"

port_open() { (exec 3<>"/dev/tcp/127.0.0.1/${PORT}") 2>/dev/null && exec 3>&- && return 0 || return 1; }

echo "==> فحص المنفذ 127.0.0.1:${PORT}"
if port_open; then
  echo "OK: هناك خدمة تستمع على ${PORT} بالفعل."
  echo "   التالي: dotnet run --project src/Ubp.Web -- --migrate"
  exit 0
fi

if command -v docker >/dev/null 2>&1; then
  echo "==> Docker موجود: تجهيز الحاوية ${CONTAINER}"
  if [ "$(docker ps -a --filter "name=^/${CONTAINER}$" --format '{{.Names}}')" = "${CONTAINER}" ]; then
    docker start "${CONTAINER}" >/dev/null
  else
    docker run -d --name "${CONTAINER}" \
      -e POSTGRES_PASSWORD="${PASSWORD}" -e POSTGRES_DB="${DB}" \
      -p "127.0.0.1:${PORT}:5432" postgres:17 >/dev/null
  fi

  for _ in $(seq 1 40); do sleep 1; port_open && break; done
  if ! port_open; then
    echo "تعذّر التشغيل — راجع: docker logs ${CONTAINER}" >&2
    exit 1
  fi

  docker exec "${CONTAINER}" createdb -U postgres "${QA_DB}" 2>/dev/null || true
  echo "OK: postgres يعمل على 127.0.0.1:${PORT} (postgres/${PASSWORD})"
  echo "   التالي: dotnet run --project src/Ubp.Web -- --migrate"
  exit 0
fi

if command -v pg_ctl >/dev/null 2>&1 || command -v psql >/dev/null 2>&1; then
  echo "==> لا يوجد Docker، لكن أدوات PostgreSQL موجودة محلياً."
  echo "   شغّل الخدمة ثم أنشئ القواعد:"
  echo "     sudo service postgresql start   # أو: brew services start postgresql@17"
  echo "     psql -h 127.0.0.1 -U postgres -c 'CREATE DATABASE ${DB};'"
  echo "     psql -h 127.0.0.1 -U postgres -c 'CREATE DATABASE ${QA_DB};'"
  exit 0
fi

echo "لا يوجد Docker ولا PostgreSQL على هذا الجهاز."
echo "  A) ثبّت Docker ثم أعد تشغيل السكربت."
echo "  B) ثبّت PostgreSQL 17 وأنشئ قاعدة ${DB} ثم: dotnet run --project src/Ubp.Web -- --migrate"
exit 1
