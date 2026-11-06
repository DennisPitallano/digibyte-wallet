-- Read-only audit of stored DigiPay webhook URLs against the outbound policy in
-- specs/008-security-hardening/contracts/webhook-url-policy.md. PostgreSQL.
-- Run by the maintainer BEFORE deploying the hotfix; it changes nothing.
--   psql "$DATABASE_URL" -f scripts/webhook-url-audit.sql
--
-- Rows returned are stores whose WebhookUrl would be refused by the new rules, so their deliveries will
-- show "destination not allowed" after deploy. Hostnames that merely RESOLVE to a private address cannot be found by SQL;
-- the dispatcher catches those at send time.

WITH parsed AS (
    SELECT
        s."Id"          AS store_id,
        s."MerchantId"  AS merchant_id,
        s."Name"        AS store_name,
        s."WebhookUrl"  AS url,
        lower(substring(s."WebhookUrl" from '^([A-Za-z][A-Za-z0-9+.-]*)://'))              AS scheme,
        -- authority = everything between "://" and the next / ? #
        substring(s."WebhookUrl" from '^[A-Za-z][A-Za-z0-9+.-]*://([^/?#]*)')               AS authority
    FROM "Stores" s
    WHERE s."WebhookUrl" IS NOT NULL AND s."WebhookUrl" <> ''
), split AS (
    SELECT
        p.*,
        position('@' in coalesce(p.authority, '')) > 0                                      AS has_userinfo,
        -- strip userinfo, then split host and port (IPv6 literals are bracketed)
        regexp_replace(coalesce(p.authority, ''), '^.*@', '')                               AS hostport
    FROM parsed p
), hosts AS (
    SELECT
        sp.*,
        CASE WHEN sp.hostport LIKE '[%' THEN substring(sp.hostport from '^\[([^\]]*)\]')
             ELSE regexp_replace(sp.hostport, ':[0-9]*$', '') END                           AS host,
        nullif(CASE WHEN sp.hostport LIKE '[%' THEN substring(sp.hostport from '\]:([0-9]+)$')
                    ELSE substring(sp.hostport from ':([0-9]+)$') END, '')::int             AS port
    FROM split sp
)
SELECT
    store_id, merchant_id, store_name, url,
    concat_ws('; ',
        CASE WHEN scheme IS DISTINCT FROM 'https' THEN 'not https' END,
        CASE WHEN has_userinfo THEN 'userinfo in URL' END,
        CASE WHEN length(url) > 2048 THEN 'longer than 2048' END,
        CASE WHEN coalesce(port, 443) NOT IN (443, 8443, 8080, 8000) THEN 'port not allowed' END,
        CASE WHEN lower(host) = 'localhost' OR lower(host) LIKE '%.localhost' OR lower(host) LIKE '%.local'
                  OR lower(host) LIKE '%.internal' THEN 'local hostname' END,
        CASE WHEN host ~ '^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$' THEN
                 CASE WHEN host ~ '^(127|10|0)\.'
                        OR host ~ '^192\.168\.'
                        OR host ~ '^169\.254\.'
                        OR host ~ '^172\.(1[6-9]|2[0-9]|3[0-1])\.'
                        OR host ~ '^100\.(6[4-9]|[7-9][0-9]|1[01][0-9]|12[0-7])\.'
                        OR host ~ '^(22[4-9]|2[3-5][0-9])\.'
                      THEN 'private/reserved IPv4 address'
                      ELSE 'IP literal' END
             WHEN host LIKE '%:%' THEN 'IPv6 literal'
        END
    ) AS problems
FROM hosts
WHERE scheme IS DISTINCT FROM 'https'
   OR has_userinfo
   OR length(url) > 2048
   OR coalesce(port, 443) NOT IN (443, 8443, 8080, 8000)
   OR lower(host) = 'localhost' OR lower(host) LIKE '%.localhost' OR lower(host) LIKE '%.local' OR lower(host) LIKE '%.internal'
   OR host ~ '^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$'
   OR host LIKE '%:%'
ORDER BY store_name;
