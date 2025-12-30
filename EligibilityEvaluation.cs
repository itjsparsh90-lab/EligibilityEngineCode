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

        // ===== WPA Rule #2: Activity Hours =====
        private const string ENT_ContactAssociation = "mcg_relationship";     // Contact Association
        private const string ENT_Income = "mcg_income";                       // Income table (work hours per week is here)
        private const string ENT_EducationDetails = "mcg_educationdetails";   // Education details (education hours per week is here)

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

        private const string FLD_DOC_Verified = "mcg_verified";
        // Citizenship is on documentextension
        private const string FLD_DOC_ChildCitizenship = "mcg_childcitizenship";
        private const string REQUIRED_CITIZENSHIP = "Montgomery";

        // Case Address fields
        private const string FLD_CA_Case = "mcg_case";
        private const string FLD_CA_EndDate = "mcg_enddate";

        // ===== WPA Rule #2: Contact Association fields =====
        private const string FLD_REL_Contact = "mcg_contactid";
        private const string FLD_REL_RelatedContact = "mcg_relatedcontactid";
        private const string FLD_REL_RoleType = "mcg_relationshiproletype"; // lookup; we will use FormattedValues
        private const string FLD_REL_EndDate = "mcg_enddate";
        private const string FLD_REL_StateCode = "statecode";

        // ===== WPA Rule #2: Income fields =====
        private const string FLD_INC_Contact = "mcg_contactid";
        private const string FLD_INC_WorkHours = "mcg_workhours"; // Work Hours per week

        // ===== WPA Rule #2: Education fields =====
        private const string FLD_EDU_Contact = "mcg_contactid";
        private const string FLD_EDU_WorkHours = "mcg_workhours"; // Work Hours (education hours per week)

        // ===== WPA Rule #2: Tokens =====
        // Rule JSON should use: token="activityrequirementmet" equals true
        private const string TOKEN_ActivityRequirementMet = "activityrequirementmet";
        private const string TOKEN_EvidenceCareNeededForChild = "evidencecareneededforchild";
        private const string TOKEN_Parent1ActivityHours = "parent1activityhoursperweek";
        private const string TOKEN_Parent2ActivityHours = "parent2activityhoursperweek";
        private const string TOKEN_ParentsTotalActivityHours = "totalactivityhoursperweek";
        private const string TOKEN_ProofIdentityProvided = "proofidentityprovided";


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
                    // Per your current implementation (do not change)
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
                    var household = GetActiveHouseholdIds(service, tracing, caseRef.Id);
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

                    // ===== Rule 2 token population (WPA Activity) =====
                    // beneficiary is recipientRef (mcg_recipientcontact)
                    if (recipientRef != null)
                    {
                        PopulateRule2Tokens_WpaActivity(service, tracing, recipientRef.Id, tokens, facts);
                        // ===== Rule 3 token population (WPA Evidence Care Needed) is derived inside Rule2 (do not override here)

                        // ===== Rule 4 token population (Proof of Identity for all household members) =====
                        var household = GetActiveHouseholdIds(service, tracing, caseRef.Id);
                        PopulateRule4Tokens_ProofOfIdentity(service, tracing, caseRef.Id, household, tokens, facts);

                    }
                }

                var evalLines = new List<EvalLine>();
                bool overall = EvaluateRuleDefinition(def, tokens, tracing, evalLines);

                // Criteria summary per top-level rule group (Q1, Q2, ...)
                var criteriaSummary = EvaluateTopLevelGroups(def, tokens, tracing);

                // Parameters considered (Rule 1 only for now) - kept unchanged
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

        private static string GetChoiceFormattedValue(Entity e, string attributeLogicalName)
        {
            if (e == null || string.IsNullOrWhiteSpace(attributeLogicalName)) return "";

            // Best: formatted label for choice/two-options
            if (e.FormattedValues != null && e.FormattedValues.ContainsKey(attributeLogicalName))
                return (e.FormattedValues[attributeLogicalName] ?? "").Trim();

            if (!e.Attributes.Contains(attributeLogicalName) || e[attributeLogicalName] == null)
                return "";

            var v = e[attributeLogicalName];

            // If someone stored it as string
            if (v is string s) return (s ?? "").Trim();

            // If choice but formatted not present, return numeric as string (fallback)
            if (v is OptionSetValue os) return os.Value.ToString(CultureInfo.InvariantCulture);

            return v.ToString().Trim();
        }


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

        private static bool HasVerifiedDocumentByCategoryAndAnySubcategory(
            IOrganizationService svc,
            ITracingService tracing,
            Guid caseId,
            Guid contactId,
            IEnumerable<string> categoryOptions,
            IEnumerable<string> subCategoryOptions)
        {
            var qe = new QueryExpression(ENT_UploadDocument)
            {
                ColumnSet = new ColumnSet(FLD_DOC_Category, FLD_DOC_SubCategory, FLD_DOC_Verified),
                TopCount = 50
            };

            qe.Criteria.AddCondition(FLD_DOC_Case, ConditionOperator.Equal, caseId);
            qe.Criteria.AddCondition(FLD_DOC_Contact, ConditionOperator.Equal, contactId);
            qe.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            qe.Criteria.AddCondition(FLD_DOC_Verified, ConditionOperator.Equal, true);

            // category options (OR)
            var catFilter = new FilterExpression(LogicalOperator.Or);
            foreach (var c in (categoryOptions ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)))
                catFilter.AddCondition(FLD_DOC_Category, ConditionOperator.Equal, c);
            if (catFilter.Conditions.Count > 0)
                qe.Criteria.AddFilter(catFilter);

            // subcategory options (OR)
            var subFilter = new FilterExpression(LogicalOperator.Or);
            foreach (var s in (subCategoryOptions ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)))
                subFilter.AddCondition(FLD_DOC_SubCategory, ConditionOperator.Equal, s);
            if (subFilter.Conditions.Count > 0)
                qe.Criteria.AddFilter(subFilter);

            var rows = svc.RetrieveMultiple(qe).Entities;
            var found = rows.Any();
            tracing.Trace($"HasVerifiedDocumentByCategoryAndAnySubcategory(case={caseId}, contact={contactId}) => {found} (rows={rows.Count})");
            return found;
        }

        private static void PopulateRule4Tokens_ProofOfIdentity(
            IOrganizationService svc,
            ITracingService tracing,
            Guid caseId,
            List<Guid> householdContactIds,
            Dictionary<string, object> tokens,
            Dictionary<string, object> facts)
        {
            // Rule 4: Proof of identity provided for all household members.
            // We treat this as: for each contact in Case Household, there must be at least one ACTIVE Case Document
            // (mcg_documentextension) for that Case + Contact that is marked Verified = Yes and is an Identity document type.

            try
            {
                if (householdContactIds == null || householdContactIds.Count == 0)
                {
                    // No household members -> cannot satisfy the rule in a meaningful way.
                    tokens[TOKEN_ProofIdentityProvided] = false;
                    facts["docs.identity.householdCount"] = 0;
                    facts["docs.identity.verifiedCount"] = 0;
                    facts["docs.identity.missingCount"] = 0;
                    facts["docs.identity.missingContacts"] = "";
                    facts["docs.identity.missingNames"] = "";
                    return;
                }

                // Identity docs: Category = Verifications, SubCategory in the allowed list.
                // NOTE: If your environment uses different labels/codes, update these lists.
                var allowedCategoryLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Verification",
            "Verifications"
        };

                var allowedSubCategoryLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Birth Certificate",
            "Driver’s License",
            "Driver's License",
            "Passport",
            "Identification Card"
        };

                // Pull active documents for this case (statecode=0) that are tied to any household member.
                // We will later filter by category/subcategory + verified.
                var qe = new QueryExpression(ENT_UploadDocument)
                {
                    ColumnSet = new ColumnSet(FLD_DOC_Contact, FLD_DOC_Case, FLD_DOC_Verified, FLD_DOC_Category, FLD_DOC_SubCategory, "statecode")
                };

                qe.Criteria.AddCondition(FLD_DOC_Case, ConditionOperator.Equal, caseId);
                qe.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0); // Active only

                // Contact IN (...) filter
                var contactFilter = new ConditionExpression(FLD_DOC_Contact, ConditionOperator.In, householdContactIds.Cast<object>().ToArray());
                qe.Criteria.AddCondition(contactFilter);

                var docs = svc.RetrieveMultiple(qe).Entities;

                // Build per-contact status
                var hasVerifiedIdentityDoc = new Dictionary<Guid, bool>();
                foreach (var id in householdContactIds) hasVerifiedIdentityDoc[id] = false;

                int verifiedCount = 0;

                foreach (var d in docs)
                {
                    var contactRef = d.GetAttributeValue<EntityReference>(FLD_DOC_Contact);
                    if (contactRef == null || contactRef.Id == Guid.Empty) continue;

                    if (!hasVerifiedIdentityDoc.ContainsKey(contactRef.Id))
                        continue; // not a household member

                    // Category/SubCategory (formatted value preferred, fallback to numeric not used here)
                    string catLabel = GetChoiceFormattedValue(d, FLD_DOC_Category);
                    string subLabel = GetChoiceFormattedValue(d, FLD_DOC_SubCategory);

                    if (string.IsNullOrWhiteSpace(catLabel) || !allowedCategoryLabels.Contains(catLabel.Trim()))
                        continue;

                    if (string.IsNullOrWhiteSpace(subLabel) || !allowedSubCategoryLabels.Contains(subLabel.Trim()))
                        continue;

                    // Verified yes/no
                    bool isVerified = IsYes(d, FLD_DOC_Verified);
                    if (!isVerified) continue;

                    if (!hasVerifiedIdentityDoc[contactRef.Id])
                    {
                        hasVerifiedIdentityDoc[contactRef.Id] = true;
                        verifiedCount++;
                    }
                }

                var missingIds = hasVerifiedIdentityDoc.Where(kvp => kvp.Value == false).Select(kvp => kvp.Key).ToList();
                var missingNames = new List<string>();

                foreach (var id in missingIds)
                {
                    var n = TryGetContactFullName(svc, tracing, id);
                    if (!string.IsNullOrWhiteSpace(n)) missingNames.Add(n);
                    else missingNames.Add(id.ToString());
                }

                bool allOk = missingIds.Count == 0;

                tokens[TOKEN_ProofIdentityProvided] = allOk;

                facts["docs.identity.householdCount"] = householdContactIds.Count;
                facts["docs.identity.verifiedCount"] = verifiedCount;
                facts["docs.identity.missingCount"] = missingIds.Count;
                facts["docs.identity.missingContacts"] = string.Join(",", missingIds);
                facts["docs.identity.missingNames"] = string.Join(", ", missingNames);

                // Add a human-friendly trace to help debugging in Plugin Trace Logs.
                tracing.Trace($"Rule4 ProofIdentityProvided: household={householdContactIds.Count}, verifiedIdentity={verifiedCount}, missing={missingIds.Count} ({facts["docs.identity.missingNames"]})");
            }
            catch (Exception ex)
            {
                tracing.Trace("PopulateRule4Tokens_ProofOfIdentity failed: " + ex);
                tokens[TOKEN_ProofIdentityProvided] = false;
                facts["docs.identity.householdCount"] = householdContactIds?.Count ?? 0;
                facts["docs.identity.verifiedCount"] = 0;
                facts["docs.identity.missingCount"] = householdContactIds?.Count ?? 0;
                facts["docs.identity.missingContacts"] = householdContactIds != null ? string.Join(",", householdContactIds) : "";
                facts["docs.identity.missingNames"] = "";
            }
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

        #region ====== Rule 2 token population (WPA Activity Hours) ======

        /// <summary>
        /// Rule 2 (WPA Activity):
        /// Beneficiary -> Contact Association (mcg_relationship) -> Father/Mother -> related parent contact
        /// Parent total hours/week = SUM(mcg_income.mcg_workhours) + SUM(mcg_educationdetails.mcg_workhours)
        /// Pass if EACH parent found (Father/Mother) has total >= 25
        /// </summary>
        /// <summary>
        /// Rule 2 (WPA Activity):
        /// Beneficiary -> Contact Association (mcg_relationship) -> Father/Mother -> related parent contact
        ///
        /// For EACH parent found:
        ///   Employment hours/week = SUM(mcg_income.mcg_workhours)  [all active income rows for that parent]
        ///   Education hours/week  = SUM(mcg_educationdetails.mcg_workhours)  [all active education rows for that parent]
        ///   Total activity hours/week = employment + education
        ///
        /// WPA pass logic (current requirement): EACH parent must have Total >= 25.
        ///
        /// Notes:
        /// - The rule JSON in PCF uses token: totalactivityhoursperweek >= 25
        ///   To represent "each parent meets 25", we set totalactivityhoursperweek = MIN(parentTotalHours).
        ///   Then (MIN >= 25) ⇢ all parents >= 25.
        /// - We also add facts for UI summary (employment/education breakdown + per-parent totals).
        /// </summary>
        private static void PopulateRule2Tokens_WpaActivity(
            IOrganizationService svc,
            ITracingService tracing,
            Guid beneficiaryContactId,
            Dictionary<string, object> tokens,
            Dictionary<string, object> facts)
        {
            var parentIds = GetActiveParentsForBeneficiary(svc, tracing, beneficiaryContactId);

            // If none found, safe default (rule fails)
            if (parentIds.Count == 0)
            {
                tokens[TOKEN_ActivityRequirementMet] = false;
                tokens[TOKEN_EvidenceCareNeededForChild] = false;
                tokens[TOKEN_Parent1ActivityHours] = 0m;
                tokens[TOKEN_Parent2ActivityHours] = 0m;

                // IMPORTANT: token used in rule JSON (min across parents; here none -> 0)
                tokens[TOKEN_ParentsTotalActivityHours] = 0m; // (this constant is totalactivityhoursperweek)

                facts["wpa.parents.count"] = 0;
                facts["wpa.activity.eachParentMeets25"] = false;
                facts["wpa.activity.employmentHoursPerWeekTotal"] = 0m;
                facts["wpa.activity.educationHoursPerWeekTotal"] = 0m;
                facts["wpa.activity.combinedHoursPerWeekTotal"] = 0m;

                tracing.Trace("Rule2: No Father/Mother parents found => activityrequirementmet=false, totalactivityhoursperweek=0");
                return;
            }

            // Parent-level totals
            var parentTotals = new List<decimal>();
            var parentWorkTotals = new List<decimal>();
            var parentEduTotals = new List<decimal>();
            var parentNames = new List<string>();

            decimal allWork = 0m;
            decimal allEdu = 0m;

            foreach (var pid in parentIds)
            {
                var work = SumIncomeWorkHoursPerWeek(svc, tracing, pid);
                var edu = SumEducationWorkHoursPerWeek(svc, tracing, pid);
                var total = work + edu;

                allWork += work;
                allEdu += edu;

                parentWorkTotals.Add(work);
                parentEduTotals.Add(edu);
                parentTotals.Add(total);

                // Name is only for UI friendliness; if missing, keep empty.
                var nm = TryGetContactFullName(svc, tracing, pid);
                parentNames.Add(nm ?? string.Empty);

                tracing.Trace($"Rule2: Parent={pid} Name='{nm}' WorkHours(sum mcg_income.mcg_workhours)={work}, EduHours(sum mcg_educationdetails.mcg_workhours)={edu}, Total={total}");
            }

            // Pass if each parent total >= 25
            bool eachParentMeets25 = parentTotals.All(h => h >= 25m);

            // Token used by some rule definitions: per-parent display
            tokens[TOKEN_ActivityRequirementMet] = eachParentMeets25;
            tokens[TOKEN_EvidenceCareNeededForChild] = eachParentMeets25;
            tokens[TOKEN_Parent1ActivityHours] = parentTotals.Count > 0 ? parentTotals[0] : 0m;
            tokens[TOKEN_Parent2ActivityHours] = parentTotals.Count > 1 ? parentTotals[1] : 0m;

            // Token used in WPA template JSON: totalactivityhoursperweek >= 25
            // To model "EACH parent must be >= 25", we store MIN(parentTotals)
            var minAcrossParents = parentTotals.Min();
            tokens[TOKEN_ParentsTotalActivityHours] = minAcrossParents;
            tokens["parentstotalactivityhoursperweek"] = allWork + allEdu; // backward-compatible alias

            // Facts for your HTML summary (case-worker friendly)
            facts["wpa.parents.count"] = parentIds.Count;
            facts["wpa.activity.eachParentMeets25"] = eachParentMeets25;

            facts["wpa.rule3.evidenceCareNeededForChild"] = eachParentMeets25;
            // Totals across parents
            facts["wpa.activity.employmentHoursPerWeekTotal"] = allWork;
            facts["wpa.activity.educationHoursPerWeekTotal"] = allEdu;
            facts["wpa.activity.combinedHoursPerWeekTotal"] = allWork + allEdu;

            // Min across parents (this is what drove the rule token)
            facts["wpa.activity.minTotalHoursPerWeekAcrossParents"] = minAcrossParents;

            // Per-parent breakdown (supports up to 2 parents for now, but keeps counts)
            if (parentTotals.Count > 0)
            {
                facts["wpa.parent1.name"] = parentNames[0];
                facts["wpa.parent1.employmentHoursPerWeek"] = parentWorkTotals[0];
                facts["wpa.parent1.educationHoursPerWeek"] = parentEduTotals[0];
                facts["wpa.parent1.totalHoursPerWeek"] = parentTotals[0];
            }
            if (parentTotals.Count > 1)
            {
                facts["wpa.parent2.name"] = parentNames[1];
                facts["wpa.parent2.employmentHoursPerWeek"] = parentWorkTotals[1];
                facts["wpa.parent2.educationHoursPerWeek"] = parentEduTotals[1];
                facts["wpa.parent2.totalHoursPerWeek"] = parentTotals[1];
            }

            tracing.Trace($"Rule2 Tokens => activityrequirementmet={eachParentMeets25}, parentCount={parentIds.Count}, minTotalAcrossParents(totalactivityhoursperweek)={minAcrossParents}, combinedAllParents={allWork + allEdu}");
        }

        private static string TryGetContactFullName(IOrganizationService svc, ITracingService tracing, Guid contactId)
        {
            try
            {
                var c = svc.Retrieve("contact", contactId, new ColumnSet("fullname"));
                return c.GetAttributeValue<string>("fullname");
            }
            catch (Exception ex)
            {
                tracing.Trace("TryGetContactFullName failed: " + ex.Message);
                return null;
            }
        }

        private static List<Guid> GetActiveParentsForBeneficiary(IOrganizationService svc, ITracingService tracing, Guid beneficiaryContactId)
        {
            var qe = new QueryExpression(ENT_ContactAssociation)
            {
                ColumnSet = new ColumnSet(
                    FLD_REL_RelatedContact,
                    FLD_REL_RoleType,
                    FLD_REL_EndDate,     // optional filter (kept)
                    FLD_REL_StateCode
                )
            };

            // Beneficiary -> association rows
            qe.Criteria.AddCondition(FLD_REL_Contact, ConditionOperator.Equal, beneficiaryContactId);

            // ✅ Only ACTIVE relationships
            qe.Criteria.AddCondition(FLD_REL_StateCode, ConditionOperator.Equal, 0);

            // Optional: EndDate is null OR EndDate >= today (keeps future-dated end valid)
            var today = DateTime.UtcNow.Date;
            var endFilter = new FilterExpression(LogicalOperator.Or);
            endFilter.AddCondition(FLD_REL_EndDate, ConditionOperator.Null);
            endFilter.AddCondition(FLD_REL_EndDate, ConditionOperator.OnOrAfter, today);
            qe.Criteria.AddFilter(endFilter);

            var rows = svc.RetrieveMultiple(qe).Entities;
            tracing.Trace($"Rule2: Active ContactAssociation rows for beneficiary={beneficiaryContactId} => {rows.Count}");

            var parents = new HashSet<Guid>();

            foreach (var row in rows)
            {
                // Relationship role display text must be Father/Mother
                if (!row.FormattedValues.TryGetValue(FLD_REL_RoleType, out var roleText) || string.IsNullOrWhiteSpace(roleText))
                    continue;

                roleText = roleText.Trim();

                if (!roleText.Equals("Father", StringComparison.OrdinalIgnoreCase) &&
                    !roleText.Equals("Mother", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var related = row.GetAttributeValue<EntityReference>(FLD_REL_RelatedContact);
                if (related == null || related.Id == Guid.Empty) continue;

                parents.Add(related.Id);
            }

            return parents.ToList();
        }


        private static decimal SumIncomeWorkHoursPerWeek(IOrganizationService svc, ITracingService tracing, Guid parentContactId)
        {
            var qe = new QueryExpression(ENT_Income)
            {
                ColumnSet = new ColumnSet(FLD_INC_WorkHours),
                TopCount = 500
            };

            qe.Criteria.AddCondition(FLD_INC_Contact, ConditionOperator.Equal, parentContactId);
            qe.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);

            var rows = svc.RetrieveMultiple(qe).Entities;
            decimal total = 0m;

            foreach (var r in rows)
            {
                total += ToDecimalSafe(r.Attributes.Contains(FLD_INC_WorkHours) ? r[FLD_INC_WorkHours] : null);
            }

            tracing.Trace($"Rule2: Income hours rows={rows.Count} parent={parentContactId} totalWorkHours={total}");
            return total;
        }

        private static decimal SumEducationWorkHoursPerWeek(IOrganizationService svc, ITracingService tracing, Guid parentContactId)
        {
            var qe = new QueryExpression(ENT_EducationDetails)
            {
                ColumnSet = new ColumnSet(FLD_EDU_WorkHours),
                TopCount = 500
            };

            qe.Criteria.AddCondition(FLD_EDU_Contact, ConditionOperator.Equal, parentContactId);
            qe.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);

            var rows = svc.RetrieveMultiple(qe).Entities;
            decimal total = 0m;

            foreach (var r in rows)
            {
                total += ToDecimalSafe(r.Attributes.Contains(FLD_EDU_WorkHours) ? r[FLD_EDU_WorkHours] : null);
            }

            tracing.Trace($"Rule2: Education hours rows={rows.Count} parent={parentContactId} totalEduHours={total}");
            return total;
        }

        private static decimal ToDecimalSafe(object raw)
        {
            if (raw == null) return 0m;
            if (raw is decimal d) return d;
            if (raw is double db) return (decimal)db;
            if (raw is float f) return (decimal)f;
            if (raw is int i) return i;
            if (raw is long l) return l;
            if (raw is Money m) return m.Value;

            if (decimal.TryParse(raw.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                return parsed;

            if (decimal.TryParse(raw.ToString(), out parsed))
                return parsed;

            return 0m;
        }

        #endregion

        #region ====== Household ======

        private List<Guid> GetActiveHouseholdIds(IOrganizationService service, ITracingService tracing, Guid caseId)
        {
            // Household table
            const string ENT_CASEHOUSEHOLD = "mcg_casehousehold";
            const string FLD_HH_CASE = "mcg_case";
            const string FLD_HH_CONTACT = "mcg_contact";
            const string FLD_STATECODE = "statecode"; // 0 = Active

            var ids = new List<Guid>();

            var qe = new Microsoft.Xrm.Sdk.Query.QueryExpression(ENT_CASEHOUSEHOLD)
            {
                ColumnSet = new Microsoft.Xrm.Sdk.Query.ColumnSet(FLD_HH_CONTACT),
                Criteria = new Microsoft.Xrm.Sdk.Query.FilterExpression(Microsoft.Xrm.Sdk.Query.LogicalOperator.And)
            };

            qe.Criteria.AddCondition(FLD_HH_CASE, Microsoft.Xrm.Sdk.Query.ConditionOperator.Equal, caseId);
            qe.Criteria.AddCondition(FLD_STATECODE, Microsoft.Xrm.Sdk.Query.ConditionOperator.Equal, 0);

            var results = service.RetrieveMultiple(qe);

            foreach (var e in results.Entities)
            {
                if (e.Contains(FLD_HH_CONTACT) && e[FLD_HH_CONTACT] is EntityReference er && er.Id != Guid.Empty)
                {
                    ids.Add(er.Id);
                }
            }

            tracing.Trace($"[Rule4] Active household contacts found: {ids.Count}");
            return ids;
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
            public string label { get; set; }
            public string @operator { get; set; }
            public object value { get; set; }
        }

        private class EvalLine
        {
            public string path { get; set; }
            public string token { get; set; }
            public string label { get; set; }
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


        private static string GetTokenLabel(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return token;

            switch (token.Trim().ToLowerInvariant())
            {
                case "applicableincome": return "Applicable income present";
                case "applicableexpense": return "Applicable expense present";
                case "applicableasset": return "Applicable asset present";
                case "totalactivityhoursperweek": return "Total parent activity hours per week (work + education)";
                case "enrolledfulltimeprogram": return "Parent enrolled in a full-time program";
                case "evidencecareneededforchild": return "Evidence care is needed for the child";
                case "proofidentityprovided": return "Proof of identity provided for all household members";
                case "proofresidencyprovided": return "Proof of residency provided for all household members";
                case "mostrecenttaxreturnprovided": return "Most recent income tax return provided";
                case "pursuingchildsupportorgoodcause": return "Pursuing child support or good cause documented";
                case "childsupportdocumentprovided": return "Child support document provided";
                case "singleparentfamily": return "Single-parent family";
                case "absentparent": return "Absent parent";
                case "medicalbillsamount": return "Medical bills amount";
                case "yearlyincome": return "Yearly eligible income";
                case "householdsize": return "Household size";
                case "householdsizeadjusted": return "Household size (adjusted)";
                case "incomecategory": return "Income category (State A-J / C / D)";
                case "incomewithinrange": return "Income within eligible range";
                case "incomebelowminc": return "Income below C minimum";
                default: return token;
            }
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

        private static bool EvaluateGroup(
    RuleGroup group,
    Dictionary<string, object> tokens,
    ITracingService tracing,
    List<EvalLine> lines,
    string parentPath)
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
                    label = GetTokenLabel(c.token),   // ✅ key change
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


        //private static string GetTokenLabel(string token)
        //{
        //    if (string.IsNullOrWhiteSpace(token)) return "";

        //    // ✅ Friendly labels for UI (fixes your Rule 3 "evidencecareneededforchild" showing raw token)
        //    switch (token.Trim().ToLowerInvariant())
        //    {
        //        case "evidencecareneededforchild":
        //            return "Evidence care is needed for the child";

        //        case "totalactivityhoursperweek":
        //            return "Total parent activity hours per week (work + education)";

        //        case "proofidentityprovided":
        //            return "Proof of identity provided for all household members";

        //        default:
        //            // fallback: show token but slightly readable
        //            return token;
        //    }
        //}


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
