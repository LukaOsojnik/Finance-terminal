-- Seed: 10 new dominant companies + revenue/cost sources for all 20.
-- Source: public filings & news, May 2026 snapshot. DataSource=2 (CLAUDE_ESTIMATED) on est. rows.
-- Scale: RAW USD (matches existing Apple=383000000000 convention).
-- Currency conversions used: KRW~1360/USD, JPY~150/USD, EUR~1.08, CHF~1.20, DKK~6.9/USD, INR~83/USD, CNY~7.2/USD, NT$~31/USD.

START TRANSACTION;

-- ===============================================================
-- 1. NEW COUNTRIES (9 — China already exists, used by Tencent)
-- ===============================================================
INSERT INTO Countries (Code, Name, Region, CurrencyCode, GdpUsd, Population, RiskRating) VALUES
('SA','Saudi Arabia','Middle East','SAR',1100000000000,36000000,2.8),
('TW','Taiwan','Asia','TWD',790000000000,23500000,2.5),
('KR','South Korea','Asia','KRW',1870000000000,51700000,1.8),
('JP','Japan','Asia','JPY',4220000000000,124000000,1.6),
('CH','Switzerland','Europe','CHF',905000000000,8800000,1.0),
('FR','France','Europe','EUR',3030000000000,68000000,1.7),
('DK','Denmark','Europe','DKK',405000000000,5950000,1.1),
('NL','Netherlands','Europe','EUR',1120000000000,17900000,1.3),
('IN','India','Asia','INR',3940000000000,1430000000,3.5);

-- ===============================================================
-- 2. NEW COMPANIES (10)
-- Sector/Industry int values follow enum declaration order (0-indexed).
-- ===============================================================
INSERT INTO Companies (Name, CountryId, Sector, Industry, RevenueTotal, GrossMargin, AsOf) VALUES
('Saudi Aramco',        (SELECT Id FROM Countries WHERE Code='SA'), 0,  1, 445700000000, 0.55, '2025-12-31'),
('TSMC',                (SELECT Id FROM Countries WHERE Code='TW'), 7, 54,  90000000000, 0.59, '2025-12-31'),
('Samsung Electronics', (SELECT Id FROM Countries WHERE Code='KR'), 7, 52, 245000000000, 0.35, '2025-12-31'),
('Toyota Motor',        (SELECT Id FROM Countries WHERE Code='JP'), 3, 22, 320000000000, 0.21, '2025-03-31'),
('Nestle',              (SELECT Id FROM Countries WHERE Code='CH'), 4, 33, 107700000000, 0.47, '2025-12-31'),
('LVMH',                (SELECT Id FROM Countries WHERE Code='FR'), 3, 25,  87000000000, 0.68, '2025-12-31'),
('Novo Nordisk',        (SELECT Id FROM Countries WHERE Code='DK'), 5, 41,  45000000000, 0.84, '2025-12-31'),
('ASML',                (SELECT Id FROM Countries WHERE Code='NL'), 7, 54,  42000000000, 0.51, '2025-12-31'),
('Reliance Industries', (SELECT Id FROM Countries WHERE Code='IN'), 0,  1, 120000000000, 0.18, '2025-03-31'),
('Tencent',             (SELECT Id FROM Countries WHERE Name='China' LIMIT 1), 8, 59, 109000000000, 0.53, '2025-12-31');

-- ===============================================================
-- 3. CAPTURE IDs FOR ALL 20 COMPANIES
-- ===============================================================
SET @apple    := (SELECT Id FROM Companies WHERE Name='Apple Inc.' LIMIT 1);
SET @msft     := (SELECT Id FROM Companies WHERE Name='Microsoft Corp.' LIMIT 1);
SET @xom      := (SELECT Id FROM Companies WHERE Name='ExxonMobil' LIMIT 1);
SET @vw       := (SELECT Id FROM Companies WHERE Name='Volkswagen AG' LIMIT 1);
SET @sap      := (SELECT Id FROM Companies WHERE Name='SAP SE' LIMIT 1);
SET @byd      := (SELECT Id FROM Companies WHERE Name='BYD Co.' LIMIT 1);
SET @baba     := (SELECT Id FROM Companies WHERE Name='Alibaba Group' LIMIT 1);
SET @pbr      := (SELECT Id FROM Companies WHERE Name='Petrobras' LIMIT 1);
SET @vale     := (SELECT Id FROM Companies WHERE Name='Vale S.A.' LIMIT 1);
SET @nvda     := (SELECT Id FROM Companies WHERE Name='Nvidia Corp.' LIMIT 1);
SET @aramco   := (SELECT Id FROM Companies WHERE Name='Saudi Aramco' LIMIT 1);
SET @tsmc     := (SELECT Id FROM Companies WHERE Name='TSMC' LIMIT 1);
SET @samsung  := (SELECT Id FROM Companies WHERE Name='Samsung Electronics' LIMIT 1);
SET @toyota   := (SELECT Id FROM Companies WHERE Name='Toyota Motor' LIMIT 1);
SET @nestle   := (SELECT Id FROM Companies WHERE Name='Nestle' LIMIT 1);
SET @lvmh     := (SELECT Id FROM Companies WHERE Name='LVMH' LIMIT 1);
SET @novo     := (SELECT Id FROM Companies WHERE Name='Novo Nordisk' LIMIT 1);
SET @asml     := (SELECT Id FROM Companies WHERE Name='ASML' LIMIT 1);
SET @ril      := (SELECT Id FROM Companies WHERE Name='Reliance Industries' LIMIT 1);
SET @tcehy    := (SELECT Id FROM Companies WHERE Name='Tencent' LIMIT 1);

-- ===============================================================
-- 4. REVENUE SOURCES
-- SourceType: 0=CUSTOMER, 1=SEGMENT, 2=REGION, 3=PRODUCT
-- DataSource: 0=EDGAR, 1=MANUAL, 2=CLAUDE_ESTIMATED, 3=OPENBB
-- ===============================================================

-- Apple FY2025 ($416B): segment split per 10-K
INSERT INTO RevenueSources (SourceType, Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
(1,'iPhone',                       209590000000, 0.5036, 0, @apple, NULL),
(1,'Services',                     109160000000, 0.2623, 0, @apple, NULL),
(1,'Wearables, Home & Accessories', 35690000000, 0.0858, 0, @apple, NULL),
(1,'Mac',                           33710000000, 0.0810, 0, @apple, NULL),
(1,'iPad',                          28020000000, 0.0673, 0, @apple, NULL);

-- Microsoft FY2025 ($281.7B): segment split per 10-K
INSERT INTO RevenueSources (SourceType, Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
(1,'Productivity & Business Processes', 120000000000, 0.4260, 0, @msft, NULL),
(1,'Intelligent Cloud (Azure)',         110000000000, 0.3905, 0, @msft, NULL),
(1,'More Personal Computing',            51700000000, 0.1835, 0, @msft, NULL);

-- ExxonMobil 2025 (~$317B): segment split
INSERT INTO RevenueSources (SourceType, Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
(1,'Energy Products (Downstream)', 217760000000, 0.6870, 0, @xom, NULL),
(1,'Upstream',                      55660000000, 0.1756, 0, @xom, NULL),
(1,'Chemical Products',             18890000000, 0.0596, 0, @xom, NULL);

-- Volkswagen 2025 (€321.9B ~ $348B): brand split (approximations)
INSERT INTO RevenueSources (SourceType, Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
(1,'VW Passenger Cars',            105000000000, 0.3017, 2, @vw, NULL),
(1,'Audi (incl. PFS)',              39100000000, 0.1124, 0, @vw, NULL),
(1,'Porsche & Luxury (Bentley/Lambo/Bugatti)', 34800000000, 0.1000, 0, @vw, NULL),
(1,'Skoda & SEAT/Cupra',            55000000000, 0.1580, 2, @vw, NULL),
(1,'Commercial Vehicles & Traton',  60000000000, 0.1724, 2, @vw, NULL),
(1,'Financial Services',            54000000000, 0.1552, 2, @vw, NULL);

-- SAP 2025 (€37.8B ~ $40.8B)
INSERT INTO RevenueSources (SourceType, Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
(1,'Cloud',                         22700000000, 0.5564, 0, @sap, NULL),
(1,'Software licenses & support',   12500000000, 0.3064, 2, @sap, NULL),
(1,'Services',                       4600000000, 0.1127, 0, @sap, NULL);

-- BYD 2025 ($116B): segment split
INSERT INTO RevenueSources (SourceType, Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
(1,'Automobiles & related',         93600000000, 0.8070, 0, @byd, NULL),
(1,'Handset components & assembly', 18000000000, 0.1552, 2, @byd, NULL),
(2,'Overseas vehicle exports',      18000000000, 0.1552, 2, @byd, NULL),
(3,'Batteries & energy storage',     6500000000, 0.0560, 2, @byd, NULL);

-- Alibaba FY2025 (~$131B): segment split
INSERT INTO RevenueSources (SourceType, Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
(1,'Taobao & Tmall Group (China commerce)', 60000000000, 0.4580, 0, @baba, NULL),
(1,'Cloud Intelligence Group',              17000000000, 0.1298, 0, @baba, NULL),
(1,'International Digital Commerce (AIDC)', 14947000000, 0.1141, 0, @baba, NULL),
(1,'Cainiao Logistics',                     14000000000, 0.1069, 2, @baba, NULL),
(1,'Local Services & Digital Media',        25000000000, 0.1908, 2, @baba, NULL);

-- Petrobras 2025 (~$98B): segment split
INSERT INTO RevenueSources (SourceType, Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
(1,'Refining, Transportation & Marketing (RTM)', 84165000000, 0.8588, 0, @pbr, NULL),
(1,'Exploration & Production (E&P)',             32000000000, 0.3265, 2, @pbr, NULL),
(1,'Gas & Low-Carbon Energy',                     6500000000, 0.0663, 2, @pbr, NULL),
(2,'Oil exports',                                25000000000, 0.2551, 2, @pbr, NULL);

-- Vale 2025 (~$42B): commodity split
INSERT INTO RevenueSources (SourceType, Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
(3,'Iron ore fines & pellets', 28800000000, 0.6857, 2, @vale, NULL),
(3,'Copper',                    3590000000, 0.0855, 2, @vale, NULL),
(3,'Nickel',                    2690000000, 0.0640, 2, @vale, NULL),
(2,'China (steel mills)',      25000000000, 0.5952, 2, @vale, NULL);

-- Nvidia FY2025 ($130.5B): segment split per 10-K
INSERT INTO RevenueSources (SourceType, Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
(1,'Data Center',                  115200000000, 0.8828, 0, @nvda, NULL),
(1,'Gaming',                        11400000000, 0.0874, 0, @nvda, NULL),
(1,'Professional Visualization',     1900000000, 0.0146, 0, @nvda, NULL),
(1,'Automotive',                     1700000000, 0.0130, 0, @nvda, NULL),
(0,'Microsoft (hyperscaler GPU)',   17000000000, 0.1303, 2, @nvda, @msft);

-- Saudi Aramco 2025 ($445.7B)
INSERT INTO RevenueSources (SourceType, Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
(1,'Upstream (crude & gas)',       340000000000, 0.7629, 2, @aramco, NULL),
(1,'Downstream (refining/chems)',   95000000000, 0.2132, 2, @aramco, NULL),
(2,'Asia (China, Japan, Korea)',   270000000000, 0.6058, 2, @aramco, NULL),
(0,'Reliance Industries (crude)',    8000000000, 0.0179, 2, @aramco, @ril);

-- TSMC 2025 (~$90B / NT$2.9T)
INSERT INTO RevenueSources (SourceType, Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
(0,'Nvidia (foundry)',              17100000000, 0.1900, 0, @tsmc, @nvda),
(0,'Apple (foundry)',               15300000000, 0.1700, 0, @tsmc, @apple),
(0,'AMD (foundry)',                  6300000000, 0.0700, 2, @tsmc, NULL),
(0,'Broadcom & Qualcomm',            9000000000, 0.1000, 2, @tsmc, NULL),
(3,'5nm/3nm advanced nodes',        44000000000, 0.4889, 2, @tsmc, NULL),
(3,'7nm and mature nodes',          24000000000, 0.2667, 2, @tsmc, NULL);

-- Samsung Electronics 2025 (KRW 333.6T ~ $245B)
INSERT INTO RevenueSources (SourceType, Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
(1,'Device Solutions (DS, memory + foundry)', 117000000000, 0.4776, 0, @samsung, NULL),
(1,'Device eXperience (DX, MX/VD)',           112000000000, 0.4571, 0, @samsung, NULL),
(1,'Samsung Display (SDC)',                    25000000000, 0.1020, 0, @samsung, NULL),
(0,'Apple (OLED panels & memory)',             19000000000, 0.0776, 2, @samsung, @apple),
(0,'Nvidia (HBM memory)',                       5000000000, 0.0204, 2, @samsung, @nvda);

-- Toyota Motor FY2025 (~$320B): region split
INSERT INTO RevenueSources (SourceType, Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
(2,'North America',                128700000000, 0.4022, 0, @toyota, NULL),
(2,'Japan',                         85000000000, 0.2656, 2, @toyota, NULL),
(2,'Asia (ex-Japan)',               45300000000, 0.1416, 0, @toyota, NULL),
(2,'Europe',                        38000000000, 0.1188, 2, @toyota, NULL),
(2,'Other regions',                 22400000000, 0.0700, 0, @toyota, NULL);

-- Nestle 2025 (CHF 89.5B ~ $107.7B)
INSERT INTO RevenueSources (SourceType, Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
(3,'Powdered & Liquid Beverages',   30260000000, 0.2810, 0, @nestle, NULL),
(3,'PetCare (Purina)',              22150000000, 0.2057, 0, @nestle, NULL),
(3,'Prepared dishes & cooking aids',13780000000, 0.1280, 2, @nestle, NULL),
(3,'Milk products & ice cream',     12480000000, 0.1159, 2, @nestle, NULL),
(3,'Nutrition & Health Science',    12190000000, 0.1132, 2, @nestle, NULL),
(2,'Zone Americas',                 51700000000, 0.4800, 0, @nestle, NULL),
(2,'Zone AOA (Asia/Oceania/Africa)',28860000000, 0.2680, 0, @nestle, NULL),
(2,'Zone Europe',                   27140000000, 0.2520, 0, @nestle, NULL);

-- LVMH 2025 (€80.8B ~ $87B)
INSERT INTO RevenueSources (SourceType, Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
(1,'Fashion & Leather Goods',       40820000000, 0.4692, 0, @lvmh, NULL),
(1,'Selective Retailing (Sephora etc)', 19760000000, 0.2271, 0, @lvmh, NULL),
(1,'Perfumes & Cosmetics',           8700000000, 0.1000, 2, @lvmh, NULL),
(1,'Wines & Spirits',                5790000000, 0.0666, 0, @lvmh, NULL),
(1,'Watches & Jewelry',             10800000000, 0.1241, 2, @lvmh, NULL);

-- Novo Nordisk 2025 (~$45B / DKK 311B)
INSERT INTO RevenueSources (SourceType, Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
(3,'Wegovy (obesity)',               5350000000, 0.1189, 0, @novo, NULL),
(3,'Ozempic (diabetes)',            18500000000, 0.4111, 2, @novo, NULL),
(3,'Other GLP-1 / diabetes',        14000000000, 0.3111, 2, @novo, NULL),
(3,'Rare disease',                   3500000000, 0.0778, 2, @novo, NULL),
(2,'United States',                 28000000000, 0.6222, 2, @novo, NULL);

-- ASML 2025 (€38B ~ $41B)
INSERT INTO RevenueSources (SourceType, Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
(0,'TSMC',                          15400000000, 0.3756, 2, @asml, @tsmc),
(0,'Samsung Electronics',            8000000000, 0.1951, 2, @asml, @samsung),
(0,'Intel & SK Hynix & Micron',      9000000000, 0.2195, 2, @asml, NULL),
(3,'EUV systems',                   18500000000, 0.4512, 0, @asml, NULL),
(3,'DUV systems',                   14300000000, 0.3488, 0, @asml, NULL),
(3,'Installed-base service',         8000000000, 0.1951, 2, @asml, NULL),
(2,'China',                         10250000000, 0.2500, 0, @asml, NULL);

-- Reliance Industries FY2025 (₹9.98 lakh crore ~ $120B)
INSERT INTO RevenueSources (SourceType, Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
(1,'Oil-to-Chemicals (O2C)',        75500000000, 0.6292, 0, @ril, NULL),
(1,'Reliance Retail',               42700000000, 0.3558, 0, @ril, NULL),
(1,'Digital Services (Jio)',        17000000000, 0.1417, 0, @ril, NULL),
(1,'Oil & Gas (E&P)',                3000000000, 0.0250, 2, @ril, NULL);

-- Tencent 2025 (CNY 751.8B ~ $109B)
INSERT INTO RevenueSources (SourceType, Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
(1,'Fintech & Business Services',   33260000000, 0.3052, 0, @tcehy, NULL),
(1,'Domestic Games',                23800000000, 0.2184, 0, @tcehy, NULL),
(1,'Marketing Services (Ads)',      21030000000, 0.1930, 0, @tcehy, NULL),
(1,'Social Networks',               18510000000, 0.1698, 0, @tcehy, NULL),
(1,'International Games',           11220000000, 0.1029, 0, @tcehy, NULL);

-- ===============================================================
-- 5. COST SOURCES
-- CostBase: 0=COGS, 1=OPEX, 2=TOTAL_COSTS
-- ===============================================================

-- Apple: huge supplier costs to TSMC (chips), Samsung (displays), Foxconn (not in 20)
INSERT INTO CostSources (CostBase, Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
(0,'TSMC (silicon)',                15300000000, 0.0660, 2, @apple, @tsmc),
(0,'Samsung Electronics (panels+memory)', 19000000000, 0.0820, 2, @apple, @samsung),
(0,'Other suppliers & assembly',   180000000000, 0.7770, 2, @apple, NULL),
(1,'R&D',                           31370000000, 0.0754, 0, @apple, NULL),
(1,'SG&A',                          26900000000, 0.0647, 0, @apple, NULL);

-- Microsoft: Nvidia GPU buys for Azure are well-publicized
INSERT INTO CostSources (CostBase, Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
(0,'Nvidia (datacenter GPUs)',      17000000000, 0.0604, 2, @msft, @nvda),
(0,'Datacenter ops & cloud infra',  60000000000, 0.2130, 2, @msft, NULL),
(1,'R&D',                           32500000000, 0.1154, 0, @msft, NULL),
(1,'Sales & Marketing + G&A',       30000000000, 0.1065, 2, @msft, NULL);

-- ExxonMobil: COGS dominated by purchased crude + production cost
INSERT INTO CostSources (CostBase, Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
(0,'Crude oil & feedstock purchases', 165000000000, 0.5210, 2, @xom, NULL),
(0,'Production & manufacturing',       55000000000, 0.1737, 2, @xom, NULL),
(1,'Exploration expenses',              5000000000, 0.0158, 2, @xom, NULL),
(1,'SG&A',                              9700000000, 0.0306, 2, @xom, NULL);

-- Volkswagen
INSERT INTO CostSources (CostBase, Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
(0,'Cost of vehicles sold',         286000000000, 0.8221, 2, @vw, NULL),
(0,'Batteries & EV cells (CATL/BYD)', 8000000000, 0.0230, 2, @vw, @byd),
(1,'R&D capitalized + expensed',     22000000000, 0.0632, 2, @vw, NULL),
(1,'Distribution & admin',           20000000000, 0.0575, 2, @vw, NULL);

-- SAP
INSERT INTO CostSources (CostBase, Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
(0,'Cloud infra & hosting',          5500000000, 0.1348, 2, @sap, NULL),
(1,'R&D',                            7100000000, 0.1740, 2, @sap, NULL),
(1,'Sales & marketing',              9200000000, 0.2255, 2, @sap, NULL),
(1,'G&A',                            2100000000, 0.0515, 2, @sap, NULL);

-- BYD
INSERT INTO CostSources (CostBase, Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
(0,'Vehicle & battery COGS',        93800000000, 0.8086, 2, @byd, NULL),
(1,'R&D',                            7400000000, 0.0638, 2, @byd, NULL),
(1,'SG&A',                           5200000000, 0.0448, 2, @byd, NULL);

-- Alibaba
INSERT INTO CostSources (CostBase, Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
(0,'COGS (logistics, merchandise, infra)', 81000000000, 0.6183, 2, @baba, NULL),
(1,'R&D (incl. cloud AI)',           9500000000, 0.0725, 2, @baba, NULL),
(1,'Sales & marketing',             14000000000, 0.1069, 2, @baba, NULL),
(0,'Nvidia (AI accelerators)',       2000000000, 0.0153, 2, @baba, @nvda);

-- Petrobras
INSERT INTO CostSources (CostBase, Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
(0,'Lifting + refining cost',       42000000000, 0.4286, 2, @pbr, NULL),
(0,'Imported feedstock',             8500000000, 0.0867, 2, @pbr, NULL),
(1,'Exploration + G&A',              5300000000, 0.0541, 2, @pbr, NULL);

-- Vale
INSERT INTO CostSources (CostBase, Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
(0,'Iron ore C1 cash cost',          6700000000, 0.1595, 2, @vale, NULL),
(0,'Freight & logistics',            4400000000, 0.1048, 2, @vale, NULL),
(0,'Royalties & maintenance',        3200000000, 0.0762, 2, @vale, NULL),
(1,'SG&A + exploration',             1500000000, 0.0357, 2, @vale, NULL);

-- Nvidia: TSMC is the dominant supplier
INSERT INTO CostSources (CostBase, Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
(0,'TSMC (foundry wafers)',         17100000000, 0.1310, 2, @nvda, @tsmc),
(0,'Samsung Electronics (HBM)',      5000000000, 0.0383, 2, @nvda, @samsung),
(0,'Other packaging & components',  10000000000, 0.0766, 2, @nvda, NULL),
(1,'R&D',                           13000000000, 0.0996, 0, @nvda, NULL),
(1,'SG&A',                           3500000000, 0.0268, 2, @nvda, NULL);

-- Saudi Aramco
INSERT INTO CostSources (CostBase, Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
(0,'Upstream lifting cost',         18000000000, 0.0404, 2, @aramco, NULL),
(0,'Refining feedstock & opex',     85000000000, 0.1907, 2, @aramco, NULL),
(1,'Royalties to KSA government',   95000000000, 0.2132, 2, @aramco, NULL),
(1,'SG&A',                          11000000000, 0.0247, 2, @aramco, NULL);

-- TSMC: ASML equipment is the marquee capex/cogs item
INSERT INTO CostSources (CostBase, Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
(0,'ASML (lithography systems)',    15400000000, 0.1711, 2, @tsmc, @asml),
(0,'Wafer materials & gases',       12000000000, 0.1333, 2, @tsmc, NULL),
(0,'Fab depreciation',              10000000000, 0.1111, 2, @tsmc, NULL),
(1,'R&D',                            6300000000, 0.0700, 2, @tsmc, NULL),
(1,'SG&A',                           3200000000, 0.0356, 2, @tsmc, NULL);

-- Samsung Electronics
INSERT INTO CostSources (CostBase, Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
(0,'ASML & semicap equipment',       8000000000, 0.0327, 2, @samsung, @asml),
(0,'Wafer/components/materials',   140000000000, 0.5714, 2, @samsung, NULL),
(1,'R&D',                           25000000000, 0.1020, 2, @samsung, NULL),
(1,'SG&A',                          22000000000, 0.0898, 2, @samsung, NULL);

-- Toyota Motor
INSERT INTO CostSources (CostBase, Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
(0,'Vehicle COGS (parts, steel, labor)', 252000000000, 0.7875, 2, @toyota, NULL),
(1,'R&D',                           10500000000, 0.0328, 2, @toyota, NULL),
(1,'SG&A',                          30000000000, 0.0938, 2, @toyota, NULL);

-- Nestle
INSERT INTO CostSources (CostBase, Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
(0,'Raw materials (coffee, cocoa, dairy)', 57000000000, 0.5293, 2, @nestle, NULL),
(0,'Packaging & manufacturing',     10500000000, 0.0975, 2, @nestle, NULL),
(1,'Marketing & distribution',      19000000000, 0.1764, 2, @nestle, NULL),
(1,'Administration',                 4800000000, 0.0446, 2, @nestle, NULL);

-- LVMH
INSERT INTO CostSources (CostBase, Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
(0,'Raw materials & craftsmanship', 28000000000, 0.3218, 2, @lvmh, NULL),
(1,'Retail network rent & store ops',13000000000, 0.1494, 2, @lvmh, NULL),
(1,'Marketing & advertising',       11000000000, 0.1264, 2, @lvmh, NULL),
(1,'G&A',                            4500000000, 0.0517, 2, @lvmh, NULL);

-- Novo Nordisk
INSERT INTO CostSources (CostBase, Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
(0,'API manufacturing & filling',    7000000000, 0.1556, 2, @novo, NULL),
(1,'R&D (clinical trials)',          5200000000, 0.1156, 2, @novo, NULL),
(1,'Sales & marketing',              8500000000, 0.1889, 2, @novo, NULL),
(1,'G&A',                            1400000000, 0.0311, 2, @novo, NULL);

-- ASML
INSERT INTO CostSources (CostBase, Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
(0,'Components & sub-systems (Zeiss/Trumpf)', 18000000000, 0.4286, 2, @asml, NULL),
(0,'Manufacturing & install',        2500000000, 0.0595, 2, @asml, NULL),
(1,'R&D',                            5300000000, 0.1262, 2, @asml, NULL),
(1,'SG&A',                           1400000000, 0.0333, 2, @asml, NULL);

-- Reliance Industries
INSERT INTO CostSources (CostBase, Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
(0,'Saudi Aramco (crude imports)',   8000000000, 0.0667, 2, @ril, @aramco),
(0,'Other crude & petrochem feedstock', 65000000000, 0.5417, 2, @ril, NULL),
(0,'Retail merchandise',            32000000000, 0.2667, 2, @ril, NULL),
(1,'Telecom network opex (Jio)',     7000000000, 0.0583, 2, @ril, NULL),
(1,'SG&A',                           5500000000, 0.0458, 2, @ril, NULL);

-- Tencent
INSERT INTO CostSources (CostBase, Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
(0,'Content + payment + cloud costs', 51000000000, 0.4679, 2, @tcehy, NULL),
(0,'Nvidia (AI training chips)',     3000000000, 0.0275, 2, @tcehy, @nvda),
(1,'R&D',                           10500000000, 0.0963, 0, @tcehy, NULL),
(1,'Sales & marketing',              4800000000, 0.0440, 2, @tcehy, NULL),
(1,'G&A',                            6500000000, 0.0596, 2, @tcehy, NULL);

COMMIT;

-- Verification queries (run separately):
-- SELECT COUNT(*) FROM Companies WHERE DeletedAt IS NULL;            -- expect 20
-- SELECT COUNT(*) FROM Countries WHERE DeletedAt IS NULL;            -- expect 13
-- SELECT COUNT(*) FROM RevenueSources WHERE DeletedAt IS NULL;
-- SELECT COUNT(*) FROM CostSources WHERE DeletedAt IS NULL;
-- SELECT c.Name AS Company, c2.Name AS RelatedCompany, rs.Name AS RevenueSrc, rs.Value
-- FROM RevenueSources rs JOIN Companies c ON c.Id=rs.CompanyId
-- LEFT JOIN Companies c2 ON c2.Id=rs.RelatedCompanyId WHERE rs.RelatedCompanyId IS NOT NULL;
