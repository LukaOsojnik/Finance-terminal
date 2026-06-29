// Service layer was split from one flat `Services` namespace into role-based subpackages.
// These project-wide usings let existing consumers and cross-package references resolve the
// service types without a per-file `using` for each subpackage.
global using simple_bloomberg_terminal.Services.ApiKeys;
global using simple_bloomberg_terminal.Services.Llm;
global using simple_bloomberg_terminal.Services.Clients.Edgar;
global using simple_bloomberg_terminal.Services.Clients.Fmp;
global using simple_bloomberg_terminal.Services.Clients.Yahoo;
global using simple_bloomberg_terminal.Services.Clients.Reference;
global using simple_bloomberg_terminal.Services.Clients.IndexSrc;
global using simple_bloomberg_terminal.Services.Extraction;
global using simple_bloomberg_terminal.Services.Discovery;
global using simple_bloomberg_terminal.Services.Provisioning;
global using simple_bloomberg_terminal.Services.Indices;
global using simple_bloomberg_terminal.Services.Classification;
global using simple_bloomberg_terminal.Services.Financials;
global using simple_bloomberg_terminal.Services.Impact;
global using simple_bloomberg_terminal.Services.Media;
global using simple_bloomberg_terminal.Services.Search;
