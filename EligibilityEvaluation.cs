using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Newtonsoft.Json;

namespace JsonWorkflowEngineRule
{
    public class EligibilityEvaluation : IPlugin
    {
        #region ====== ACTION PARAMS ======

        private const string IN_CaseBenefitLineItemId = "CaseBenifitLineItemId"; 
        private const string IN_EvaluationContextJson = "EvaluationContextJson";

        private const string OUT_IsEligible = "iseligible";
        private const string OUT_ResultMessage = "resultmessage";
        private const string OUT_ResultJson = "resultJson";

        #endregion

        #region ====== ENTITIES ======

        private const string ENT_BenefitLineItem = "mcg_casebenefitplanlineitem";
        private const string ENT_Case = "incident";
        private const string ENT_ServiceScheme = "mcg_servicescheme";

        private const string ENT_CaseHousehold = "mcg_casehousehold";
        private const string ENT_CaseIncome = "mcg_caseincome";
        private const string ENT_UploadDocument = "mcg_documentextension";

        private const string ENT_Address = "mcg_address";

        #endregion

        #region ====== FIELDS ======

        // BLI fields
        private const string FLD_BLI_RegardingCase = "mcg_regardingincident";  
        private const string FLD_BLI_Verified = "mcg_verified";
        private const string FLD_BLI_Benefit = "mcg_servicebenefitnames";      
        private const string FLD_BLI_RecipientContact = "mcg_recipientcontact";

        // ✅ Care validations (you confirmed)
        private const string FLD_BLI_CareServiceType = "mcg_careservicetype";
        private const string FLD_BLI_CareServiceLevel = "mcg_careservicelevel";

        // Service Scheme fields
        private const string FLD_SCHEME_BenefitName = "mcg_benefitname";       
        private const string FLD_SCHEME_RuleJson = "mcg_ruledefinitionjson";

        // Case fields
        private const string FLD_CASE_PrimaryContact = "mcg_contact";
        private const string FLD_CASE_YearlyHouseholdIncome = "mcg_yearlyhouseholdincome";

        // Contact fields
        private const string FLD_CONTACT_MaritalStatus = "familystatuscode";
        private const string FLD_CONTACT_PrimaryAddress = "mcg_primaryaddress";

        // Address fields
        private const string FLD_ADDR_CountyText = "mcg_countytext";
        private const string REQUIRED_COUNTY = "Montgomery";

        // Household fields 
        private const string FLD_CH_Case = "mcg_case"; 
        private const string FLD_CH_Contact = "mcg_contact";
        private const string FLD_CH_DateEntered = "mcg_dateentered";
        private const string FLD_CH_DateExited = "mcg_dateexited";
        private const string FLD_CH_Primary = "mcg_primary";
        private const string FLD_CH_StateCode = "statecode";

        // Case Income fields 
        private const string FLD_CI_Case = "mcg_case"; 
        private const string FLD_CI_ApplicableIncome = "mcg_applicableincome"; 
        private const string FLD_CI_Amount = "mcg_amount"; 
        private const string FLD_CASE_YearlyEligibleIncome = "mcg_yearlyeligibleincome"; 


        // Document Extension 
        private const string FLD_DOC_Case = "mcg_case"; 
        private const string FLD_DOC_Contact = "mcg_contact"; 
        private const string FLD_DOC_Category = "mcg_uploaddocumentcategory";
        private const string FLD_DOC_SubCategory = "mcg_uploaddocumentsubcategory";

        #endregion

        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            var serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var service = serviceFactory.CreateOrganizationService(context.UserId);

            tracing.Trace("=== EligibilityEvaluationPlugin START ===");

            try
            {
               
                var bliId = GetGuidFromInput(context, IN_CaseBenefitLineItemId);

                var evalContextJson = context.InputParameters.Contains(IN_EvaluationContextJson)
                    ? context.InputParameters[IN_EvaluationContextJson] as string
                    : null;

                tracing.Trace($"Input BLI Id: {bliId}");
                tracing.Trace($"EvaluationContextJson present: {!string.IsNullOrWhiteSpace(evalContextJson)}");

                
                var bli = service.Retrieve(ENT_BenefitLineItem, bliId, new ColumnSet(
                    FLD_BLI_RegardingCase,
                    FLD_BLI_Verified,
                    FLD_BLI_Benefit,
                    FLD_BLI_RecipientContact,
                    FLD_BLI_CareServiceType,
                    FLD_BLI_CareServiceLevel
                ));

                var validationFailures = new List<string>();

                var caseRef = bli.GetAttributeValue<EntityReference>(FLD_BLI_RegardingCase);
                if (caseRef == null)
                    validationFailures.Add("Benefit Line Item must be linked to a Case.");

                var benefitRef = bli.GetAttributeValue<EntityReference>(FLD_BLI_Benefit);
                if (benefitRef == null)
                    validationFailures.Add("Financial Benefit (mcg_servicebenefitnames) is missing on Benefit Line Item.");


                if (!bli.Attributes.Contains(FLD_BLI_CareServiceType) || bli[FLD_BLI_CareServiceType] == null)
                    validationFailures.Add("Care/Service Type (mcg_careservicetype) is missing for the selected child.");

                if (!bli.Attributes.Contains(FLD_BLI_CareServiceLevel) || bli[FLD_BLI_CareServiceLevel] == null)
                    validationFailures.Add("Care/Service Level (mcg_careservicelevel) is missing for the selected child.");

                
                if (!bli.Attributes.Contains(FLD_BLI_Verified) || bli[FLD_BLI_Verified] == null)
                    validationFailures.Add("Verified (mcg_verified) is not set for the selected child.");

                // -------- Load Case + Primary Contact (if case exists) --------
                Entity inc = null;
                EntityReference primaryContactRef = null;

                if (caseRef != null)
                {
                    inc = service.Retrieve(ENT_Case, caseRef.Id, new ColumnSet(
                        FLD_CASE_PrimaryContact,
                        FLD_CASE_YearlyHouseholdIncome
                    ));

                    primaryContactRef = inc.GetAttributeValue<EntityReference>(FLD_CASE_PrimaryContact);
                    if (primaryContactRef == null)
                        validationFailures.Add("Primary contact is missing on the Case.");
                }

                // -------- ✅ Fetch Service Scheme using Benefit Id 
                string ruleJson = null;
                Entity scheme = null;

                if (benefitRef != null)
                {
                    scheme = GetServiceSchemeForBenefit(service, tracing, benefitRef.Id);
                    if (scheme == null)
                    {
                        validationFailures.Add("No Service Scheme found for the selected Financial Benefit (mcg_servicescheme.mcg_benefitname).");
                    }
                    else
                    {
                        ruleJson = scheme.GetAttributeValue<string>(FLD_SCHEME_RuleJson);
                        if (string.IsNullOrWhiteSpace(ruleJson))
                            validationFailures.Add("Rule Definition JSON (mcg_ruledefinitionjson) is missing on the Service Scheme.");
                    }
                }

                // -------- Existing validations: household + income + documents --------
                if (caseRef != null)
                {
                    var household = GetActiveHousehold(service, tracing, caseRef.Id);
                    if (household.Count == 0)
                        validationFailures.Add("No active Case Household members found (Date Exited is blank).");

                    var caseIncomeRows = GetCaseIncomeRows(service, tracing, caseRef.Id);

                    // Applicable income present 
                    var hasApplicableIncomeRow = caseIncomeRows.Any(r =>
                    {
                        if (!r.Attributes.Contains(FLD_CI_ApplicableIncome) || r[FLD_CI_ApplicableIncome] == null) return false;
                        if (r[FLD_CI_ApplicableIncome] is bool bb) return bb;
                        if (r[FLD_CI_ApplicableIncome] is OptionSetValue) return true;
                        return true;
                    });

                    var caseYearlyEligibleIncome = inc?.GetAttributeValue<Money>(FLD_CASE_YearlyEligibleIncome);
                    var hasYearlyEligibleIncomeOnCase = caseYearlyEligibleIncome != null;

                    if (!hasApplicableIncomeRow && !hasYearlyEligibleIncomeOnCase)
                    {
                        validationFailures.Add("Case Income – Applicable case Income is missing (no applicable income rows, and yearly eligible income is missing on Case).");
                    }


                    if (primaryContactRef != null)
                    {
                        // your existing required docs checks
                        if (!HasDocument(service, tracing, caseRef.Id, primaryContactRef.Id, "Identity", null))
                            validationFailures.Add("Proof of identity document is missing.");
                        if (!HasDocument(service, tracing, caseRef.Id, primaryContactRef.Id, "Residency", null))
                            validationFailures.Add("Proof of residency document is missing.");
                        if (!HasDocument(service, tracing, caseRef.Id, primaryContactRef.Id, "Income", "Tax Return"))
                            validationFailures.Add("Most recent income tax return document is missing.");

                        if (IsSingleParentMaritalScenario(service, tracing, primaryContactRef))
                        {
                            if (!HasDocument(service, tracing, caseRef.Id, primaryContactRef.Id, "Income", "Child Support"))
                                validationFailures.Add("Child support document is missing (Income > Child Support).");
                        }
                    }
                }

                // ✅ Address County validation 
                var countyFail = ValidateRecipientCountyIsMontgomery(service, tracing, bli);
                if (!string.IsNullOrWhiteSpace(countyFail))
                    validationFailures.Add(countyFail);

                // -------- Stop if validations failed --------
                if (validationFailures.Count > 0)
                {
                    tracing.Trace("VALIDATION FAILED. Returning validation failures only.");
                    context.OutputParameters[OUT_IsEligible] = false;
                    context.OutputParameters[OUT_ResultMessage] = "Validation failed. Please fix the issues and try again.";
                    context.OutputParameters[OUT_ResultJson] = BuildResultJson(validationFailures, null);
                    return;
                }

                //Rule evaluation
                var def = ParseRuleDefinition(ruleJson);
                var tokens = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

                var evalLines = new List<EvalLine>();
                bool overall = EvaluateRuleDefinition(def, tokens, tracing, evalLines);

                context.OutputParameters[OUT_IsEligible] = overall;
                context.OutputParameters[OUT_ResultMessage] = overall ? "Eligible" : "Not Eligible";
                context.OutputParameters[OUT_ResultJson] = BuildResultJson(new List<string>(), evalLines);

                tracing.Trace("Eligibility evaluation completed.");
            }
            catch (Exception ex)
            {
                tracing.Trace("ERROR: " + ex);
                throw new InvalidPluginExecutionException("Eligibility evaluation failed: " + ex.Message, ex);
            }
            finally
            {
                tracing.Trace("=== EligibilityEvaluationPlugin END ===");
            }
        }

        #region ====== Scheme Fetch (Benefit -> Scheme) ======

        private static Entity GetServiceSchemeForBenefit(IOrganizationService svc, ITracingService tracing, Guid benefitId)
        {
            // Find the latest active scheme for benefit (if multiples exist)
            var qe = new QueryExpression(ENT_ServiceScheme)
            {
                ColumnSet = new ColumnSet(FLD_SCHEME_RuleJson, FLD_SCHEME_BenefitName),
                TopCount = 1
            };

            qe.Criteria.AddCondition(FLD_SCHEME_BenefitName, ConditionOperator.Equal, benefitId);
            qe.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0); // Active

            qe.Orders.Add(new OrderExpression("createdon", OrderType.Descending));

            var scheme = svc.RetrieveMultiple(qe).Entities.FirstOrDefault();
            tracing.Trace($"GetServiceSchemeForBenefit: benefitId={benefitId}, found={(scheme != null)}");
            return scheme;
        }

        #endregion

        #region ====== Address Validation (Recipient -> Primary Address -> CountyText) ======

        private static string ValidateRecipientCountyIsMontgomery(IOrganizationService svc, ITracingService tracing, Entity bli)
        {
            try
            {
                var recipientRef = bli.GetAttributeValue<EntityReference>(FLD_BLI_RecipientContact);
                if (recipientRef == null)
                    return "Beneficiary (Recipient Contact) is missing on Benefit Line Item.";

                var contact = svc.Retrieve("contact", recipientRef.Id, new ColumnSet(FLD_CONTACT_PrimaryAddress));
                var addrRef = contact.GetAttributeValue<EntityReference>(FLD_CONTACT_PrimaryAddress);

                if (addrRef == null)
                    return "Beneficiary primary address is missing on Recipient Contact.";

                var addr = svc.Retrieve(ENT_Address, addrRef.Id, new ColumnSet(FLD_ADDR_CountyText));
                var county = (addr.GetAttributeValue<string>(FLD_ADDR_CountyText) ?? "").Trim();

                if (string.IsNullOrWhiteSpace(county))
                    return "County is missing on Beneficiary primary address.";

                if (!string.Equals(county, REQUIRED_COUNTY, StringComparison.OrdinalIgnoreCase))
                    return $"Beneficiary county must be '{REQUIRED_COUNTY}', but found '{county}'.";

                tracing.Trace($"PASS: Beneficiary county validated. County='{county}'.");
                return null;
            }
            catch (Exception ex)
            {
                tracing.Trace("ValidateRecipientCountyIsMontgomery ERROR: " + ex);
                return "Unable to validate beneficiary address county due to an internal error.";
            }
        }

        #endregion

        #region ====== Household / Income / Docs ======

        private static List<Entity> GetActiveHousehold(IOrganizationService svc, ITracingService tracing, Guid caseId)
        {
            var qe = new QueryExpression(ENT_CaseHousehold)
            {
                ColumnSet = new ColumnSet(
                    FLD_CH_Contact, FLD_CH_DateEntered, FLD_CH_DateExited, FLD_CH_Primary, FLD_CH_StateCode
                )
            };

            qe.Criteria.AddCondition(FLD_CH_Case, ConditionOperator.Equal, caseId);
            qe.Criteria.AddCondition(FLD_CH_StateCode, ConditionOperator.Equal, 0);
            qe.Criteria.AddCondition(FLD_CH_DateExited, ConditionOperator.Null);

            var results = svc.RetrieveMultiple(qe).Entities.ToList();
            tracing.Trace($"Active household count: {results.Count}");
            return results;
        }

        private static List<Entity> GetCaseIncomeRows(IOrganizationService svc, ITracingService tracing, Guid caseId)
        {
            var qe = new QueryExpression(ENT_CaseIncome)
            {
                ColumnSet = new ColumnSet(FLD_CI_ApplicableIncome, FLD_CI_Amount)
            };
            qe.Criteria.AddCondition(FLD_CI_Case, ConditionOperator.Equal, caseId);

            var results = svc.RetrieveMultiple(qe).Entities.ToList();
            tracing.Trace($"Case income rows count: {results.Count}");
            return results;
        }

        private static bool HasDocument(IOrganizationService svc, ITracingService tracing, Guid caseId, Guid contactId, string category, string subCategoryOrNull)
        {
            var qe = new QueryExpression(ENT_UploadDocument)
            {
                ColumnSet = new ColumnSet("createdon"),
                TopCount = 1
            };

            qe.Criteria.AddCondition(FLD_DOC_Case, ConditionOperator.Equal, caseId);
            qe.Criteria.AddCondition(FLD_DOC_Contact, ConditionOperator.Equal, contactId);
            qe.Criteria.AddCondition(FLD_DOC_Category, ConditionOperator.Equal, category);

            if (!string.IsNullOrWhiteSpace(subCategoryOrNull))
                qe.Criteria.AddCondition(FLD_DOC_SubCategory, ConditionOperator.Equal, subCategoryOrNull);

            var found = svc.RetrieveMultiple(qe).Entities.Any();
            tracing.Trace($"HasDocument({category}/{subCategoryOrNull}) = {found}");
            return found;
        }

        private static bool IsSingleParentMaritalScenario(IOrganizationService svc, ITracingService tracing, EntityReference contactRef)
        {
            var contact = svc.Retrieve("contact", contactRef.Id, new ColumnSet(FLD_CONTACT_MaritalStatus));
            if (!contact.Contains(FLD_CONTACT_MaritalStatus)) return false;

            string label = null;
            if (contact.FormattedValues.ContainsKey(FLD_CONTACT_MaritalStatus))
                label = contact.FormattedValues[FLD_CONTACT_MaritalStatus];

            tracing.Trace($"Marital status label={label}");

            if (!string.IsNullOrWhiteSpace(label))
            {
                var l = label.Trim().ToLowerInvariant();
                return l.Contains("divorc") || l.Contains("separat") || l.Contains("never") || l.Contains("single");
            }

            return false;
        }

        #endregion

        #region ====== Input / Result JSON ======

        private static Guid GetGuidFromInput(IPluginExecutionContext context, string paramName)
        {
            if (!context.InputParameters.Contains(paramName) || context.InputParameters[paramName] == null)
                throw new InvalidPluginExecutionException($"Missing required input parameter: {paramName}");

            var raw = context.InputParameters[paramName].ToString();
            if (!Guid.TryParse(raw, out var id))
                throw new InvalidPluginExecutionException($"Invalid GUID in parameter {paramName}: {raw}");

            return id;
        }

        private static string BuildResultJson(List<string> validationFailures, List<EvalLine> evaluationLines)
        {
            var payload = new
            {
                validationFailures = validationFailures ?? new List<string>(),
                lines = evaluationLines ?? new List<EvalLine>()
            };

            return JsonConvert.SerializeObject(payload);
        }

        #endregion

        #region ====== Rule JSON + Evaluator (same as before) ======

        private class RuleDefinition
        {
            public string @operator { get; set; } // "AND" | "OR"
            public List<RuleGroup> groups { get; set; }
        }

        private class RuleGroup
        {
            public string id { get; set; }
            public string label { get; set; }
            public string @operator { get; set; } // "AND" | "OR"
            public List<Condition> conditions { get; set; }
            public List<RuleGroup> groups { get; set; }
        }

        private class Condition
        {
            public string id { get; set; }
            public string token { get; set; }
            public string @operator { get; set; }
            public object value { get; set; }
        }

        private class EvalLine
        {
            public string path { get; set; }
            public string token { get; set; }
            public string op { get; set; }
            public object expected { get; set; }
            public object actual { get; set; }
            public bool pass { get; set; }
        }

        private static RuleDefinition ParseRuleDefinition(string ruleJson)
        {
            if (string.IsNullOrWhiteSpace(ruleJson))
                return new RuleDefinition { @operator = "AND", groups = new List<RuleGroup>() };

            try
            {
                var def = JsonConvert.DeserializeObject<RuleDefinition>(ruleJson);
                if (def == null || def.groups == null) return new RuleDefinition { @operator = "AND", groups = new List<RuleGroup>() };
                if (string.IsNullOrWhiteSpace(def.@operator)) def.@operator = "AND";
                return def;
            }
            catch
            {
                return new RuleDefinition { @operator = "AND", groups = new List<RuleGroup>() };
            }
        }

        private static bool EvaluateRuleDefinition(RuleDefinition def, Dictionary<string, object> tokens, ITracingService tracing, List<EvalLine> lines)
        {
            var rootAnd = string.Equals(def.@operator, "AND", StringComparison.OrdinalIgnoreCase);

            var results = new List<bool>();
            foreach (var g in def.groups ?? new List<RuleGroup>())
                results.Add(EvaluateGroup(g, tokens, tracing, lines, "ROOT"));

            return rootAnd ? results.All(x => x) : results.Any(x => x);
        }

        private static bool EvaluateGroup(RuleGroup group, Dictionary<string, object> tokens, ITracingService tracing, List<EvalLine> lines, string parentPath)
        {
            var groupPath = $"{parentPath} > {(string.IsNullOrWhiteSpace(group.label) ? group.id : group.label)}";
            var isAnd = string.Equals(group.@operator, "AND", StringComparison.OrdinalIgnoreCase);

            var localResults = new List<bool>();

            foreach (var c in group.conditions ?? new List<Condition>())
            {
                var pass = EvaluateCondition(c, tokens, out var actual);
                localResults.Add(pass);

                lines.Add(new EvalLine
                {
                    path = groupPath,
                    token = c.token,
                    op = c.@operator,
                    expected = c.value,
                    actual = actual,
                    pass = pass
                });
            }

            foreach (var child in group.groups ?? new List<RuleGroup>())
                localResults.Add(EvaluateGroup(child, tokens, tracing, lines, groupPath));

            var result = isAnd ? localResults.All(x => x) : localResults.Any(x => x);
            tracing.Trace($"Group '{groupPath}' => {result} (op={group.@operator})");
            return result;
        }

        private static bool EvaluateCondition(Condition c, Dictionary<string, object> tokens, out object actual)
        {
            tokens.TryGetValue(c.token ?? "", out actual);
            var op = (c.@operator ?? "").Trim().ToLowerInvariant();

            switch (op)
            {
                case "equals":
                case "=":
                    return AreEqual(actual, c.value);
                case "notequals":
                case "!=":
                    return !AreEqual(actual, c.value);
                case ">=":
                case "greaterorequal":
                    return CompareNumber(actual, c.value, (a, b) => a >= b);
                case "<=":
                case "lessorequal":
                    return CompareNumber(actual, c.value, (a, b) => a <= b);
                case ">":
                case "greaterthan":
                    return CompareNumber(actual, c.value, (a, b) => a > b);
                case "<":
                case "lessthan":
                    return CompareNumber(actual, c.value, (a, b) => a < b);
                default:
                    return false;
            }
        }

        private static bool AreEqual(object actual, object expected)
        {
            if (actual == null && expected == null) return true;
            if (actual == null || expected == null) return false;

            if (TryBool(actual, out var ab) && TryBool(expected, out var eb))
                return ab == eb;

            if (TryDecimal(actual, out var ad) && TryDecimal(expected, out var ed))
                return ad == ed;

            return string.Equals(actual.ToString(), expected.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        private static bool CompareNumber(object actual, object expected, Func<decimal, decimal, bool> cmp)
        {
            if (!TryDecimal(actual, out var a)) return false;
            if (!TryDecimal(expected, out var b)) return false;
            return cmp(a, b);
        }

        private static bool TryDecimal(object v, out decimal d)
        {
            d = 0m;
            if (v == null) return false;

            if (v is decimal dd) { d = dd; return true; }
            if (v is double db) { d = (decimal)db; return true; }
            if (v is float f) { d = (decimal)f; return true; }
            if (v is int i) { d = i; return true; }
            if (v is long l) { d = l; return true; }
            if (v is Money m) { d = m.Value; return true; }

            return decimal.TryParse(v.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out d);
        }

        private static bool TryBool(object v, out bool b)
        {
            b = false;
            if (v == null) return false;

            if (v is bool bb) { b = bb; return true; }
            if (v is string s && bool.TryParse(s, out var parsed)) { b = parsed; return true; }
            if (v is int i) { b = i != 0; return true; }
            if (v is long l) { b = l != 0; return true; }

            return false;
        }

        #endregion
    }
}
