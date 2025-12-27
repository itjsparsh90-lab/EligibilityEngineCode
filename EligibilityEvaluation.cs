using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
        private const string ENT_CaseExpense = "mcg_caseexpense";

        private const string ENT_UploadDocument = "mcg_documentextension";

        private const string ENT_CaseAddress = "mcg_caseaddress";

        #endregion

        #region ====== FIELDS ======

        // BLI fields
        private const string FLD_BLI_RegardingCase = "mcg_regardingincident";

        // Verified? is Choice
        private const string FLD_BLI_Verified = "mcg_verifiedids";

        // Choice values (from your screenshot)
        private const int VERIFIED_NO = 568020000;
        private const int VERIFIED_YES = 568020001;

        private const string FLD_BLI_Benefit = "mcg_servicebenefitnames";
        private const string FLD_BLI_RecipientContact = "mcg_recipientcontact";

        // Care validations
        private const string FLD_BLI_CareServiceType = "mcg_careservicetype";
        private const string FLD_BLI_CareServiceLevel = "mcg_careservicelevel";

        // Service Scheme fields
        private const string FLD_SCHEME_BenefitName = "mcg_benefitname";
        private const string FLD_SCHEME_RuleJson = "mcg_ruledefinitionjson";

        // Case fields
        private const string FLD_CASE_PrimaryContact = "mcg_contact";

        // Household fields
        private const string FLD_CH_Case = "mcg_case";
        private const string FLD_CH_Contact = "mcg_contact";
        private const string FLD_CH_DateEntered = "mcg_dateentered";
        private const string FLD_CH_DateExited = "mcg_dateexited";
        private const string FLD_CH_Primary = "mcg_primary";
        private const string FLD_CH_StateCode = "statecode";

        // Case Income fields
        private const string FLD_CI_Case = "mcg_case";
        private const string FLD_CI_ApplicableIncome = "mcg_applicableincome"; // income applicable

        // Expense uses same logical name for applicable flag (your update)
        private const string FLD_Common_Case = "mcg_case";

        // Document Extension (category/subcategory are TEXT fields)
        private const string FLD_DOC_Case = "mcg_case";
        private const string FLD_DOC_Contact = "mcg_contact";
        private const string FLD_DOC_Category = "mcg_uploaddocumentcategory";
        private const string FLD_DOC_SubCategory = "mcg_uploaddocumentsubcategory";

        // Citizenship is on documentextension
        private const string FLD_DOC_ChildCitizenship = "mcg_childcitizenship";
        private const string REQUIRED_CITIZENSHIP = "Montgomery";

        // Case Address fields
        private const string FLD_CA_Case = "mcg_case";
        private const string FLD_CA_EndDate = "mcg_enddate";

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

                // Care validations
                if (!bli.Attributes.Contains(FLD_BLI_CareServiceType) || bli[FLD_BLI_CareServiceType] == null)
                    validationFailures.Add("Care/Service Type (mcg_careservicetype) is missing for the selected child.");

                if (!bli.Attributes.Contains(FLD_BLI_CareServiceLevel) || bli[FLD_BLI_CareServiceLevel] == null)
                    validationFailures.Add("Care/Service Level (mcg_careservicelevel) is missing for the selected child.");

                // Verified? (Choice)
                OptionSetValue verifiedOs = bli.GetAttributeValue<OptionSetValue>(FLD_BLI_Verified);

                bool verifiedIsYes = false;
                if (verifiedOs == null)
                {
                    validationFailures.Add("Verified? (mcg_verifiedids) is not set for the selected child.");
                }
                else if (verifiedOs.Value == VERIFIED_YES)
                {
                    verifiedIsYes = true;
                    tracing.Trace("Verified? = YES => Documented.");
                }
                else if (verifiedOs.Value == VERIFIED_NO)
                {
                    // Per your latest requirement: still show note, but validation should block? (you asked earlier to show as validation)
                    validationFailures.Add("Verified is No, so the user is Undocumented.");
                }
                else
                {
                    validationFailures.Add($"Verified? has an unexpected value: {verifiedOs.Value}.");
                }

                // Recipient / Beneficiary contact
                var recipientRef = bli.GetAttributeValue<EntityReference>(FLD_BLI_RecipientContact);
                if (recipientRef == null)
                {
                    validationFailures.Add("Beneficiary (Recipient Contact) is missing on Benefit Line Item.");
                }

                // -------- Load Case --------
                Entity inc = null;
                EntityReference primaryContactRef = null;

                if (caseRef != null)
                {
                    inc = service.Retrieve(ENT_Case, caseRef.Id, new ColumnSet(FLD_CASE_PrimaryContact));
                    primaryContactRef = inc.GetAttributeValue<EntityReference>(FLD_CASE_PrimaryContact);
                    if (primaryContactRef == null)
                        validationFailures.Add("Primary contact is missing on the Case.");
                }

                // -------- Fetch Service Scheme using Benefit Id --------
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

                // -------- Validations --------
                if (caseRef != null)
                {
                    // household
                    var household = GetActiveHousehold(service, tracing, caseRef.Id);
                    if (household.Count == 0)
                        validationFailures.Add("No active Case Household members found (Date Exited is blank).");

                    // Validation #2: ANY Case Income row exists (per your change)
                    var hasAnyIncome = HasAnyCaseIncome(service, tracing, caseRef.Id);
                    if (!hasAnyIncome)
                        validationFailures.Add("Case Income – No case income record found.");

                    // Validation #3: Case Address exists with null/future end date
                    var addressFail = ValidateCaseHomeAddress(service, tracing, caseRef.Id);
                    if (!string.IsNullOrWhiteSpace(addressFail))
                        validationFailures.Add(addressFail);

                    // Validation #1: Citizenship read from Birth Certificate doc (mcg_documentextension)
                    if (recipientRef != null)
                    {
                        var citizenshipFail = ValidateChildCitizenshipFromBirthCertificate(service, tracing, recipientRef.Id);
                        if (!string.IsNullOrWhiteSpace(citizenshipFail))
                            validationFailures.Add(citizenshipFail);
                    }

                    // Proof of Address and Tax Returns (TEXT category/subcategory)
                    if (primaryContactRef != null)
                    {
                        if (!HasDocumentByCategorySubcategory(service, tracing, caseRef.Id, primaryContactRef.Id, "Identification", "Proof of Address"))
                            validationFailures.Add("Proof of address document is missing.");

                        if (!HasDocumentByCategorySubcategory(service, tracing, caseRef.Id, primaryContactRef.Id, "Income", "Tax Returns"))
                            validationFailures.Add("Most recent income tax return document is missing.");
                    }
                }

                // -------- Stop if validations failed --------
                if (validationFailures.Count > 0)
                {
                    tracing.Trace("VALIDATION FAILED. Returning validation failures only.");

                    context.OutputParameters[OUT_IsEligible] = false;
                    context.OutputParameters[OUT_ResultMessage] = "Validation failed. Please fix the issues and try again.";

                    context.OutputParameters[OUT_ResultJson] = BuildResultJson(
                        validationFailures,
                        evaluationLines: null,
                        criteriaSummary: null,
                        parametersConsidered: null,
                        isEligible: false,
                        resultMessage: "Validation failed."
                    );

                    return;
                }

                // -------- Rule evaluation --------
                var def = ParseRuleDefinition(ruleJson);
                var tokens = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

                // Add small “facts” (helps summary UI; safe even if you don’t show it)
                var facts = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                facts["benefit.verifiedFlag"] = verifiedIsYes ? "Yes" : "No";

                if (caseRef != null)
                {
                    // Rule 1 token population (income + expense; asset ignored)
                    PopulateRule1Tokens(service, tracing, caseRef.Id, tokens);
                }

                var evalLines = new List<EvalLine>();
                bool overall = EvaluateRuleDefinition(def, tokens, tracing, evalLines);

                // Criteria summary per top-level rule group (Q1, Q2, ...)
                var criteriaSummary = EvaluateTopLevelGroups(def, tokens, tracing);

                // Parameters considered (Rule 1 only for now)
                var parametersConsidered = BuildParametersConsideredForRule1(tokens);

                context.OutputParameters[OUT_IsEligible] = overall;
                context.OutputParameters[OUT_ResultMessage] = overall ? "Eligible" : "Not Eligible";

                context.OutputParameters[OUT_ResultJson] = BuildResultJson(
                    validationFailures: new List<string>(),
                    evaluationLines: evalLines,
                    criteriaSummary: criteriaSummary,
                    parametersConsidered: parametersConsidered,
                    isEligible: overall,
                    resultMessage: overall ? "Eligible" : "Not Eligible",
                    facts: facts
                );

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

        #region ====== VALIDATIONS ======

        private static bool HasAnyCaseIncome(IOrganizationService svc, ITracingService tracing, Guid caseId)
        {
            var qe = new QueryExpression(ENT_CaseIncome)
            {
                ColumnSet = new ColumnSet("mcg_caseincomeid"),
                TopCount = 1
            };

            qe.Criteria.AddCondition(FLD_CI_Case, ConditionOperator.Equal, caseId);

            var found = svc.RetrieveMultiple(qe).Entities.Any();
            tracing.Trace($"HasAnyCaseIncome(caseId={caseId}) = {found}");
            return found;
        }

        private static string ValidateCaseHomeAddress(IOrganizationService svc, ITracingService tracing, Guid caseId)
        {
            try
            {
                var qe = new QueryExpression(ENT_CaseAddress)
                {
                    ColumnSet = new ColumnSet(FLD_CA_EndDate)
                };
                qe.Criteria.AddCondition(FLD_CA_Case, ConditionOperator.Equal, caseId);

                var rows = svc.RetrieveMultiple(qe).Entities.ToList();
                tracing.Trace($"CaseAddress rows found: {rows.Count}");

                if (rows.Count == 0)
                    return "Home address is missing on Case (no mcg_caseaddress records found).";

                var today = DateTime.UtcNow.Date;

                bool hasActive = rows.Any(r =>
                {
                    var end = r.GetAttributeValue<DateTime?>(FLD_CA_EndDate);
                    return !end.HasValue || end.Value.Date >= today;
                });

                if (!hasActive)
                    return "Home address is missing on Case (no address with a Null or Future End Date).";

                tracing.Trace("PASS: Case home address validation.");
                return null;
            }
            catch (Exception ex)
            {
                tracing.Trace("ValidateCaseHomeAddress ERROR: " + ex);
                return "Unable to validate Case Home Address due to an internal error.";
            }
        }

        private static string ValidateChildCitizenshipFromBirthCertificate(IOrganizationService svc, ITracingService tracing, Guid beneficiaryContactId)
        {
            try
            {
                var qe = new QueryExpression(ENT_UploadDocument)
                {
                    ColumnSet = new ColumnSet("createdon", FLD_DOC_ChildCitizenship),
                    TopCount = 1
                };

                qe.Criteria.AddCondition(FLD_DOC_Contact, ConditionOperator.Equal, beneficiaryContactId);
                qe.Criteria.AddCondition(FLD_DOC_Category, ConditionOperator.Equal, "Verifications");
                qe.Criteria.AddCondition(FLD_DOC_SubCategory, ConditionOperator.Equal, "Birth Certificate");
                qe.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);

                qe.Orders.Add(new OrderExpression("createdon", OrderType.Descending));

                var doc = svc.RetrieveMultiple(qe).Entities.FirstOrDefault();
                var hasBirthCert = (doc != null);

                tracing.Trace($"Birth Certificate doc found for beneficiary={beneficiaryContactId}: {hasBirthCert}");

                if (!hasBirthCert)
                    return "No document is present for beneficiary under Verifications > Birth Certificate.";

                var citizenship = (doc.GetAttributeValue<string>(FLD_DOC_ChildCitizenship) ?? "").Trim();

                if (string.IsNullOrWhiteSpace(citizenship))
                    return "Child citizenship is missing on Birth Certificate document (mcg_childcitizenship).";

                if (!string.Equals(citizenship, REQUIRED_CITIZENSHIP, StringComparison.OrdinalIgnoreCase))
                    return $"Child citizenship does not match {REQUIRED_CITIZENSHIP} (Current: {citizenship}).";

                tracing.Trace($"PASS: Child citizenship validated from document. Citizenship='{citizenship}'.");
                return null;
            }
            catch (Exception ex)
            {
                tracing.Trace("ValidateChildCitizenshipFromBirthCertificate ERROR: " + ex);
                return "Unable to validate Child Citizenship due to an internal error.";
            }
        }

        private static bool HasDocumentByCategorySubcategory(IOrganizationService svc, ITracingService tracing, Guid caseId, Guid contactId, string category, string subCategory)
        {
            var qe = new QueryExpression(ENT_UploadDocument)
            {
                ColumnSet = new ColumnSet("createdon"),
                TopCount = 1
            };

            qe.Criteria.AddCondition(FLD_DOC_Case, ConditionOperator.Equal, caseId);
            qe.Criteria.AddCondition(FLD_DOC_Contact, ConditionOperator.Equal, contactId);
            qe.Criteria.AddCondition(FLD_DOC_Category, ConditionOperator.Equal, category);
            qe.Criteria.AddCondition(FLD_DOC_SubCategory, ConditionOperator.Equal, subCategory);
            qe.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);

            var found = svc.RetrieveMultiple(qe).Entities.Any();
            tracing.Trace($"HasDocumentByCategorySubcategory(case={caseId}, contact={contactId}, {category}/{subCategory}) = {found}");
            return found;
        }

        #endregion

        #region ====== Rule 1 token population (Income + Expense only) ======

        private static void PopulateRule1Tokens(IOrganizationService svc, ITracingService tracing, Guid caseId, Dictionary<string, object> tokens)
        {
            tokens["applicableincome"] = HasActiveApplicableIncome(svc, tracing, caseId);
            tokens["applicableexpense"] = HasActiveApplicableExpense(svc, tracing, caseId);

            tracing.Trace($"Rule1 Tokens => applicableincome={tokens["applicableincome"]}, applicableexpense={tokens["applicableexpense"]}");
        }

        // Robust Yes check: supports Two Options + Choice (uses FormattedValues "Yes"/"No")
        private static bool IsYes(Entity row, string attributeLogicalName)
        {
            if (row == null) return false;
            if (!row.Attributes.Contains(attributeLogicalName) || row[attributeLogicalName] == null) return false;

            // Best: FormattedValue for choice/two-options
            if (row.FormattedValues != null && row.FormattedValues.ContainsKey(attributeLogicalName))
            {
                var fmt = (row.FormattedValues[attributeLogicalName] ?? "").Trim();
                if (fmt.Equals("Yes", StringComparison.OrdinalIgnoreCase)) return true;
                if (fmt.Equals("No", StringComparison.OrdinalIgnoreCase)) return false;
            }

            var v = row[attributeLogicalName];

            if (v is bool b) return b;

            if (v is OptionSetValue os)
            {
                // fallback (most environments Yes=1) - formatted value above is preferred
                return os.Value == 1;
            }

            if (v is int i) return i == 1;
            if (v is long l) return l == 1;

            var s = v.ToString();
            return s.Equals("true", StringComparison.OrdinalIgnoreCase) || s.Equals("yes", StringComparison.OrdinalIgnoreCase) || s == "1";
        }

        private static bool HasActiveApplicableIncome(IOrganizationService svc, ITracingService tracing, Guid caseId)
        {
            var qe = new QueryExpression(ENT_CaseIncome)
            {
                ColumnSet = new ColumnSet("mcg_caseincomeid", FLD_CI_ApplicableIncome, "statecode"),
                TopCount = 50
            };

            qe.Criteria.AddCondition(FLD_CI_Case, ConditionOperator.Equal, caseId);
            qe.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);

            var rows = svc.RetrieveMultiple(qe).Entities;
            var found = rows.Any(r => IsYes(r, FLD_CI_ApplicableIncome));

            tracing.Trace($"HasActiveApplicableIncome(caseId={caseId}) rows={rows.Count} => {found}");
            return found;
        }

        private static bool HasActiveApplicableExpense(IOrganizationService svc, ITracingService tracing, Guid caseId)
        {
            var qe = new QueryExpression(ENT_CaseExpense)
            {
                ColumnSet = new ColumnSet("mcg_caseexpenseid", FLD_CI_ApplicableIncome, "statecode"),
                TopCount = 50
            };

            qe.Criteria.AddCondition(FLD_Common_Case, ConditionOperator.Equal, caseId);
            qe.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);

            var rows = svc.RetrieveMultiple(qe).Entities;
            var found = rows.Any(r => IsYes(r, FLD_CI_ApplicableIncome));

            tracing.Trace($"HasActiveApplicableExpense(caseId={caseId}) rows={rows.Count} => {found}");
            return found;
        }

        #endregion

        #region ====== Household ======

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

        private static string BuildResultJson(
            List<string> validationFailures,
            List<EvalLine> evaluationLines,
            List<CriteriaSummaryLine> criteriaSummary,
            List<string> parametersConsidered,
            bool isEligible,
            string resultMessage,
            Dictionary<string, object> facts = null)
        {
            var payload = new
            {
                validationFailures = validationFailures ?? new List<string>(),
                criteriaSummary = criteriaSummary ?? new List<CriteriaSummaryLine>(),
                parametersConsidered = parametersConsidered ?? new List<string>(),
                lines = evaluationLines ?? new List<EvalLine>(),

                // NEW (safe extra fields)
                isEligible = isEligible,
                resultMessage = resultMessage ?? "",
                facts = facts ?? new Dictionary<string, object>()
            };

            return JsonConvert.SerializeObject(payload);
        }

        #endregion

        #region ====== Rule JSON + Evaluator ======

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

        private class CriteriaSummaryLine
        {
            public string id { get; set; }
            public string label { get; set; }
            public bool pass { get; set; }
        }

        private static List<CriteriaSummaryLine> EvaluateTopLevelGroups(
            RuleDefinition def,
            Dictionary<string, object> tokens,
            ITracingService tracing)
        {
            var summary = new List<CriteriaSummaryLine>();
            if (def?.groups == null) return summary;

            foreach (var g in def.groups)
            {
                bool groupPass = EvaluateGroup(g, tokens, tracing, new List<EvalLine>(), "ROOT");
                summary.Add(new CriteriaSummaryLine
                {
                    id = g.id,
                    label = g.label,
                    pass = groupPass
                });

                tracing.Trace($"CRITERIA SUMMARY: {g.id} '{g.label}' => {(groupPass ? "PASS" : "FAIL")}");
            }

            return summary;
        }

        private static List<string> BuildParametersConsideredForRule1(Dictionary<string, object> tokens)
        {
            string YesNo(object v)
            {
                if (v is bool b) return b ? "Yes" : "No";
                return (v?.ToString() ?? "");
            }

            return new List<string>
            {
                $"Applicable Income present (Active + Applicable) = {YesNo(tokens.ContainsKey("applicableincome") ? tokens["applicableincome"] : null)}",
                $"Applicable Expense present (Active + Applicable) = {YesNo(tokens.ContainsKey("applicableexpense") ? tokens["applicableexpense"] : null)}"
            };
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
