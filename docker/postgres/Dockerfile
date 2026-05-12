# PostgreSQL для локальной разработки Onlyspans.Processes.
# Соответствует ConnectionStrings в appsettings*.json (порт 5432, пользователь postgres).

FROM postgres:16-alpine

ENV POSTGRES_USER=postgres
ENV POSTGRES_PASSWORD=postgres
ENV POSTGRES_DB=processes

# Дополнительная БД для Development (appsettings.Development.json)
COPY docker-entrypoint-initdb.d/ /docker-entrypoint-initdb.d/

EXPOSE 5432
