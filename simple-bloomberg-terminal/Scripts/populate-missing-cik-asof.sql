-- Populate missing Cik + AsOf for Companies.
-- Cik = SEC EDGAR identifier (10-digit zero-padded). Non-SEC-registered filers marked 0000000000 - review before relying on for API calls.
-- AsOf set to 2026-05-21 only where currently NULL.

UPDATE Companies SET Cik = '0000320193' WHERE Id = 1;  -- Apple Inc.
UPDATE Companies SET Cik = '0000789019' WHERE Id = 2;  -- Microsoft Corp.
UPDATE Companies SET Cik = '0000034088' WHERE Id = 3;  -- ExxonMobil
UPDATE Companies SET Cik = '0000875400' WHERE Id = 4;  -- Volkswagen AG
UPDATE Companies SET Cik = '0001000184' WHERE Id = 5;  -- SAP SE
UPDATE Companies SET Cik = '0001463757' WHERE Id = 6;  -- BYD Co.
UPDATE Companies SET Cik = '0001577552' WHERE Id = 7;  -- Alibaba Group
UPDATE Companies SET Cik = '0001119639' WHERE Id = 8;  -- Petrobras
UPDATE Companies SET Cik = '0000917851' WHERE Id = 9;  -- Vale S.A.
UPDATE Companies SET Cik = '0001045810' WHERE Id = 10; -- Nvidia Corp.
UPDATE Companies SET Cik = '0000000000' WHERE Id = 11; -- Saudi Aramco (not SEC-registered)
UPDATE Companies SET Cik = '0001046179' WHERE Id = 12; -- TSMC
UPDATE Companies SET Cik = '0000000000' WHERE Id = 13; -- Samsung Electronics (not SEC-registered)
UPDATE Companies SET Cik = '0001094517' WHERE Id = 14; -- Toyota Motor
UPDATE Companies SET Cik = '0000000000' WHERE Id = 15; -- Nestle (not SEC-registered)
UPDATE Companies SET Cik = '0000000000' WHERE Id = 16; -- LVMH (not SEC-registered)
UPDATE Companies SET Cik = '0000353278' WHERE Id = 17; -- Novo Nordisk
UPDATE Companies SET Cik = '0000937966' WHERE Id = 18; -- ASML
UPDATE Companies SET Cik = '0000000000' WHERE Id = 19; -- Reliance Industries (not SEC-registered)
UPDATE Companies SET Cik = '0000000000' WHERE Id = 20; -- Tencent (not SEC-registered)

UPDATE Companies SET AsOf = '2026-05-21' WHERE AsOf IS NULL AND DeletedAt IS NULL;
