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

        private const string ENT_EligibilityAdmin = "mcg_eligibilityadmin";
        private const string ENT_EligibilityIncomeRange = "mcg_eligibilityincomerange";
        private const string ENT_SubsidyTableName = "mcg_subsidytablename";
        private const string ENT_ContactTableName = "contact";
        private const string ENT_CaseInvolvedParties = "mcg_caseinvolvedparties";
        private const string ENT_RelationshipRole = "mcg_relationshiprole";

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

        //Contact fields
        private const string FLD_Con_MaritalStatus = "familystatuscode";

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
        private const string FLD_CASE_IncidentId = "incidentid";
        private const string FLD_CASE_YearlyEligibleIncome = "mcg_yearlyeligibleincome";
        private const string FLD_CASE_YearlyHouseholdIncome = "mcg_yearlyhouseholdincome";

        // Household fields
        private const string FLD_CH_Case = "mcg_case";
        private const string FLD_CH_Contact = "mcg_contact";
        private const string FLD_CH_DateEntered = "mcg_dateentered";
        private const string FLD_CH_DateExited = "mcg_dateexited";
        private const string FLD_CH_Primary = "mcg_primary";
        private const string FLD_CH_StateCode = "statecode";
        private const string FLD_CH_Relationship = "mcg_relationship";
        private const string FLD_CH_RelationshipRole = "mcg_relationshiprole";

        // Case Income fields
        private const string FLD_CI_Case = "mcg_case";
        private const string FLD_CI_ApplicableIncome = "mcg_applicableincome"; // income applicable
        private const string FLD_CI_IncomeCategory = "mcg_incomecategory";
        private const string FLD_CI_IncomeSubCategory = "mcg_incomesubcategory";

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

        //Eligibility Income Range fields
        private const string FLD_EIR_HouseHoldSize = "mcg_householdsize";
        private const string FLD_EIR_MinIncome = "mcg_minincome";
        private const string FLD_EIR_MaxIncome = "mcg_maxincome";
        private const string FLD_EIR_EligibilityAdmin = "mcg_eligibilityadmin";

        //EligibiliyAdmin
        private const string FLD_EA_Name = "mcg_name";

        //Case Already receiving State CCS benefits field
        private const string FLD_Case_StateCCSFlag = "mcg_alreadyreceivingstateccsbenefits";

        //Case Expense Table
        private const string FLD_CE_ExpenseType = "mcg_type";
        private const string FLD_CE_Amount = "mcg_amount";

        //Case Involved Parties Fields
        private const string FLD_CIP_CaseRelationShip = "mcg_caserelationship";
        private const string FLD_CIP_CaseId = "mcg_incident";

        //Relationship role entity
        private const string FLD_RR_RRID = "mcg_relationshiproleid";
        private const string FLD_RR_Name = "mcg_name";

        //Enum Expense Type
        public enum CaseExpenseType
        {
            MedicalBills = 861450021,
            MedicalPremiumExcludingMedicare = 861450022,
            MedicarePremium = 861450023
        }

        //Enum RelationType Type
        public enum HouseholdRelationship
        {
            SpouseOrPartner = 861450037
        }

        //Enum Involved Parties Case Relationship Choice
        public enum InvolvedPartiesRelationship
        {
            SpouseOrPartner = 861450037,
            OtherParent = 861450007,
            Parent = 861450025,
            OtherFamilyMember = 861450039
        }

        //Enum MaritalStatus
        public enum ContactMaritalStatus
        {
            Single = 1,
            Divorced = 3,
            Separated = 861450003,
            SingleOrNeverMarried = 861450000
        }

        //Enum Income Category from Case income entity
        public enum CaseIncomeCatergory
        {
            EarningsOrWages = 861450000,
            Military = 861450001,
            PublicBenefits = 861450002,
            Other = 861450004
        }


        //Enum Income Sub Category from Case income entity
        public enum CaseIncomeSubCatergory
        {
            ChildSupport = 861450045
        }

        //Enum Case Entity State CCS choice column
        public enum CaseStateCCS
        {
            Yes = 568020000,
            NO = 568020001
        }

        //Static Relationship lookup
        public static class CaseRelationShipLookup
        {
            public const string SpouseOrPartner = "Spouse/Partner";
            public const string DomesticPartner = "Domestic Partner";
        }

        //Static DocumentCategory Type
        public static class DocumentCategory
        {
            public const string Income = "Income";
            public const string Expenses = "Expenses";
        }

        //Static DocumentSubCategory Type
        public static class DocumentSubCategory
        {
            public const string Paystub = "Paystub";
            public const string W2 = "W-2";
            public const string Expense = "Expense";
            public const string ChildSupport = "Child Support";
        }

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
        private const string TOKEN_MostRecentTaxReturnProvided = "mostrecenttaxreturnprovided";


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
                    var hasAnyIncome = HasAnyCaseIncome(service, tracing, caseRef.Id, null, null);
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
                    // Has Already receiving the State CCS check
                    var stateCcsMessage = HasAlreadyReceivingStateCCS(service, tracing, caseRef.Id);
                    if (!string.IsNullOrWhiteSpace(stateCcsMessage))
                    {
                        validationFailures.Add(stateCcsMessage);
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

                    //Rule 7 token population (Yeary income)
                    PopulateRule7Tokens(context, service, tracing, caseRef.Id, tokens);

                    //Rule 8 token population (Child support, court ordered or voluntary child support?)
                    PopulateRule8Tokens(context, service, tracing, caseRef.Id, tokens);

                    //Rule 9 token population (Medical expense > 2500)
                    PopulateRule9Tokens(context, service, tracing, caseRef.Id, tokens);

                    //Rule 10 token population (Single-parent family)
                    PopulateRule10Tokens(context, service, tracing, caseRef.Id, tokens);

                    // ===== Rule 2 token population (WPA Activity) =====
                    // beneficiary is recipientRef (mcg_recipientcontact)
                    if (recipientRef != null)
                    {
                        PopulateRule2Tokens_WpaActivity(service, tracing, recipientRef.Id, tokens, facts);
                        // ===== Rule 3 token population (WPA Evidence Care Needed) is derived inside Rule2 (do not override here)

                        // ===== Rule 4 token population (Proof of Identity for all household members) =====
                        var household = GetActiveHouseholdIds(service, tracing, caseRef.Id);
                        PopulateRule4Tokens_ProofOfIdentity(service, tracing, caseRef.Id, household, tokens, facts);

                        // ===== Rule 5 token population (Proof of Residency) =====
                        PopulateRule5Tokens_ProofOfResidency(service, tracing, caseRef.Id, tokens, facts);

                        // ===== Rule 6 token population (Most recent income tax return) =====
                        PopulateRule6Tokens_MostRecentIncomeTaxReturn(service, tracing, caseRef.Id, tokens, facts);

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

        private static bool HasAnyCaseIncome(IOrganizationService svc, ITracingService tracing, Guid caseId, CaseIncomeCatergory[] caseIncomeCategories, CaseIncomeSubCatergory[] caseIncomeSubCategories)
        {
            var qe = new QueryExpression(ENT_CaseIncome)
            {
                ColumnSet = new ColumnSet("mcg_caseincomeid"),
                TopCount = 1
            };

            qe.Criteria.AddCondition(FLD_CI_Case, ConditionOperator.Equal, caseId);

            bool hasCategories = caseIncomeCategories?.Any() == true;
            bool hasSubCategories = caseIncomeSubCategories?.Any() == true;

            if (hasCategories && hasSubCategories)
            {
                var dependentFilter = new FilterExpression(LogicalOperator.And);

                dependentFilter.AddCondition(
                    FLD_CI_IncomeCategory,
                    ConditionOperator.In,
                    caseIncomeCategories.Select(c => (object)(int)c).ToArray());

                dependentFilter.AddCondition(
                    FLD_CI_IncomeSubCategory,
                    ConditionOperator.In,
                    caseIncomeSubCategories.Select(sc => (object)(int)sc).ToArray());

                qe.Criteria.AddFilter(dependentFilter);

                tracing.Trace("Applied IncomeCategory and IncomeSubCategory filters");
            }
            else
            {
                tracing.Trace("No IncomeCategory and SubCategory filters applied");
            }


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

        private static string HasAlreadyReceivingStateCCS(IOrganizationService svc, ITracingService tracing, Guid caseId)
        {
            var qe = new QueryExpression(ENT_Case)
            {
                ColumnSet = new ColumnSet(FLD_Case_StateCCSFlag, "statecode"),
                TopCount = 1
            };

            qe.Criteria.AddCondition(FLD_CASE_IncidentId, ConditionOperator.Equal, caseId);
            qe.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);

            var entity = svc.RetrieveMultiple(qe).Entities.FirstOrDefault();

            if (entity == null)
            {
                tracing.Trace("Case not found");
                return string.Empty;
            }

            if (!entity.Attributes.Contains(FLD_Case_StateCCSFlag))
            {
                tracing.Trace("CaseStateCCS is NULL");
                return "Please fill Already receiving State CCS benefits question in case form";
            }

            var stateCcs = entity.GetAttributeValue<OptionSetValue>(FLD_Case_StateCCSFlag)?.Value;

            if (stateCcs == (int)CaseStateCCS.Yes)
            {
                tracing.Trace("CaseStateCCS = Yes");
                return "You are already receiving the benefit from the State CCS";
            }

            tracing.Trace("CaseStateCCS is present but not Yes");
            return string.Empty;
        }

        private static bool CheckMaritalStatusAllowedFromCaseContact(IOrganizationService svc, ITracingService tracing, Guid caseId, params ContactMaritalStatus[] maritalStatus)
        {
            tracing.Trace("CheckMaritalStatusAllowedFromCase method start");

            Entity caseRecord = svc.Retrieve(
                ENT_Case,
                caseId,
                new ColumnSet(FLD_CASE_PrimaryContact));

            EntityReference primaryContactRef = caseRecord.GetAttributeValue<EntityReference>(FLD_CASE_PrimaryContact);

            bool isMaritalStatus = false;

            if (primaryContactRef == null)
            {
                tracing.Trace("Primary contact is missing on the Case.");
            }
            else
            {
                Entity contact = svc.Retrieve(
                    ENT_ContactTableName,
                    primaryContactRef.Id,
                    new ColumnSet(FLD_Con_MaritalStatus));

                OptionSetValue maritalStatusValue =
                    contact.GetAttributeValue<OptionSetValue>(FLD_Con_MaritalStatus);

                if (maritalStatusValue != null &&
                    maritalStatus
                        .Select(ms => (int)ms)
                        .Contains(maritalStatusValue.Value))
                {
                    isMaritalStatus = true;
                }

                tracing.Trace($"Marital status allowed: {isMaritalStatus}");
            }

            tracing.Trace("CheckMaritalStatusAllowedFromCase method end");
            return isMaritalStatus;
        }

        private static List<Entity> CheckCaseInvolvedParties(IOrganizationService svc, ITracingService tracing, Guid caseId, InvolvedPartiesRelationship[] caseRelationship)
        {
            tracing.Trace($"CheckCaseInvolvedParties Method is called");
            var qe = new QueryExpression(ENT_CaseInvolvedParties)
            {
                ColumnSet = new ColumnSet(
                    FLD_CIP_CaseRelationShip, FLD_CIP_CaseId
                )
            };

            qe.Criteria.AddCondition(FLD_CIP_CaseId, ConditionOperator.Equal, caseId);
            qe.Criteria.AddCondition(FLD_CH_StateCode, ConditionOperator.Equal, 0);
            if (caseRelationship != null && caseRelationship.Length > 0)
            {
                qe.Criteria.AddCondition(
                   FLD_CIP_CaseRelationShip,
                   ConditionOperator.In,
                   caseRelationship.Select(r => (object)(int)r).ToArray()
               );
            }

            var results = svc.RetrieveMultiple(qe).Entities.ToList();
            tracing.Trace($"CheckCaseInvolvedParties count: {results.Count}");
            tracing.Trace($"CheckCaseInvolvedParties Method is end");
            return results;

        }

        private static bool HasDocumentByCategorySubcategory(
    IOrganizationService svc,
    ITracingService tracing,
    Guid caseId,
    Guid? contactId,
    string category,
    string subCategory)
        {
            var qe = new QueryExpression(ENT_UploadDocument)
            {
                ColumnSet = new ColumnSet("createdon"),
                TopCount = 1
            };

            qe.Criteria.AddCondition(FLD_DOC_Case, ConditionOperator.Equal, caseId);

            // ✅ FIX: Only filter by contact when contactId is provided
            if (contactId.HasValue && contactId.Value != Guid.Empty)
            {
                qe.Criteria.AddCondition(FLD_DOC_Contact, ConditionOperator.Equal, contactId.Value);
            }
            else
            {
                // contactId null means: "any contact" (don’t apply a contact filter)
                tracing.Trace("HasDocumentByCategorySubcategory: contactId is null/empty => not filtering by contact (ANY contact).");
            }

            qe.Criteria.AddCondition(FLD_DOC_Category, ConditionOperator.Equal, category);
            qe.Criteria.AddCondition(FLD_DOC_SubCategory, ConditionOperator.Equal, subCategory);
            qe.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);

            var found = svc.RetrieveMultiple(qe).Entities.Any();
            var contactText = (contactId.HasValue && contactId.Value != Guid.Empty) ? contactId.Value.ToString() : "ANY";
            tracing.Trace($"HasDocumentByCategorySubcategory(case={caseId}, contact={contactText}, {category}/{subCategory}) = {found}");
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
            try
            {
                // Token name used in your rule JSON
                const string TOKEN_ProofIdentityProvidedLocal = "proofidentityprovided";

                if (householdContactIds == null || householdContactIds.Count == 0)
                {
                    tokens[TOKEN_ProofIdentityProvidedLocal] = false;

                    facts["docs.identity.householdCount"] = 0;
                    facts["docs.identity.verifiedCount"] = 0;
                    facts["docs.identity.missingCount"] = 0;
                    facts["docs.identity.missingNames"] = "";
                    return;
                }

                // Allowed identity doc types (based on your functional screenshot text)
                var allowedCategory = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Verification",
            "Verifications"
        };

                var allowedSubCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Birth Certificate",
            "Driver’s License",
            "Driver's License",
            "Passport",
            "Identification Card"
        };

                // Pull docs for this case + household contacts (Active only)
                var qe = new QueryExpression(ENT_UploadDocument)
                {
                    ColumnSet = new ColumnSet(FLD_DOC_Contact, FLD_DOC_Category, FLD_DOC_SubCategory, FLD_DOC_Verified),
                    Criteria = new FilterExpression(LogicalOperator.And)
                };

                qe.Criteria.AddCondition(FLD_DOC_Case, ConditionOperator.Equal, caseId);
                qe.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);

                // Contact IN household ids
                qe.Criteria.AddCondition(new ConditionExpression(
                    FLD_DOC_Contact,
                    ConditionOperator.In,
                    householdContactIds.Cast<object>().ToArray()
                ));

                var docs = svc.RetrieveMultiple(qe).Entities;

                // Initialize per-contact "has verified identity doc"
                var hasDoc = householdContactIds.ToDictionary(id => id, id => false);

                foreach (var d in docs)
                {
                    var contactRef = d.GetAttributeValue<EntityReference>(FLD_DOC_Contact);
                    if (contactRef == null || contactRef.Id == Guid.Empty) continue;
                    if (!hasDoc.ContainsKey(contactRef.Id)) continue;

                    var cat = (d.GetAttributeValue<string>(FLD_DOC_Category) ?? "").Trim();
                    var sub = (d.GetAttributeValue<string>(FLD_DOC_SubCategory) ?? "").Trim();

                    if (!allowedCategory.Contains(cat)) continue;
                    if (!allowedSubCategories.Contains(sub)) continue;

                    var verified = IsYes(d, FLD_DOC_Verified);
                    if (!verified) continue;

                    hasDoc[contactRef.Id] = true;
                }

                var missingIds = hasDoc.Where(x => !x.Value).Select(x => x.Key).ToList();
                var missingNames = new List<string>();

                foreach (var id in missingIds)
                {
                    var name = TryGetContactFullName(svc, tracing, id);
                    missingNames.Add(string.IsNullOrWhiteSpace(name) ? id.ToString() : name);
                }

                var allOk = missingIds.Count == 0;

                tokens[TOKEN_ProofIdentityProvidedLocal] = allOk;

                facts["docs.identity.householdCount"] = householdContactIds.Count;
                facts["docs.identity.verifiedCount"] = householdContactIds.Count - missingIds.Count;
                facts["docs.identity.missingCount"] = missingIds.Count;
                facts["docs.identity.missingNames"] = string.Join(", ", missingNames);

                tracing.Trace($"[Rule4] proofidentityprovided={allOk}; household={householdContactIds.Count}; missing={missingIds.Count}; missingNames={facts["docs.identity.missingNames"]}");
            }
            catch (Exception ex)
            {
                tracing.Trace("[Rule4] PopulateRule4Tokens_ProofOfIdentity failed: " + ex);
                tokens["proofidentityprovided"] = false;
                facts["docs.identity.missingNames"] = "";
            }
        }

        private static void PopulateRule5Tokens_ProofOfResidency(
    IOrganizationService svc,
    ITracingService tracing,
    Guid caseId,
    Dictionary<string, object> tokens,
    Dictionary<string, object> facts)
        {
            try
            {
                const string TOKEN_ProofResidencyProvidedLocal = "proofresidencyprovided";

                // 1) Case Address check (active = enddate null or future)
                var qeAddr = new QueryExpression(ENT_CaseAddress)
                {
                    ColumnSet = new ColumnSet(FLD_CA_EndDate),
                    Criteria = new FilterExpression(LogicalOperator.And)
                };
                qeAddr.Criteria.AddCondition(FLD_CA_Case, ConditionOperator.Equal, caseId);

                var addresses = svc.RetrieveMultiple(qeAddr).Entities.ToList();
                var today = DateTime.UtcNow.Date;

                bool hasActiveAddress = addresses.Any(a =>
                {
                    var end = a.GetAttributeValue<DateTime?>(FLD_CA_EndDate);
                    return !end.HasValue || end.Value.Date >= today;
                });

                facts["rule5.caseAddress.count"] = addresses.Count;
                facts["rule5.caseAddress.hasActive"] = hasActiveAddress;

                if (!hasActiveAddress)
                {
                    tokens[TOKEN_ProofResidencyProvidedLocal] = false;
                    facts["rule5.docs.hasVerifiedResidencyDoc"] = false;
                    facts["rule5.docs.matchedDocInfo"] = "";
                    tracing.Trace("[Rule5] No active case address => proofresidencyprovided=false");
                    return;
                }

                // 2) Supporting document (verified) for the CASE (any contact)
                // Category/SubCategory are TEXT fields in your table.
                // Verified is Yes/No (Two Options).
                var qeDoc = new QueryExpression(ENT_UploadDocument)
                {
                    ColumnSet = new ColumnSet(FLD_DOC_Category, FLD_DOC_SubCategory, FLD_DOC_Verified, FLD_DOC_Contact),
                    Criteria = new FilterExpression(LogicalOperator.And),
                    TopCount = 200
                };

                qeDoc.Criteria.AddCondition(FLD_DOC_Case, ConditionOperator.Equal, caseId);
                qeDoc.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
                qeDoc.Criteria.AddCondition(FLD_DOC_Verified, ConditionOperator.Equal, true);

                var docs = svc.RetrieveMultiple(qeDoc).Entities;

                bool match = false;
                string matchedInfo = "";

                foreach (var d in docs)
                {
                    var cat = (d.GetAttributeValue<string>(FLD_DOC_Category) ?? "").Trim();
                    var sub = (d.GetAttributeValue<string>(FLD_DOC_SubCategory) ?? "").Trim();

                    bool ok =
                        (cat.Equals("Identification", StringComparison.OrdinalIgnoreCase) &&
                         sub.Equals("Proof of Address", StringComparison.OrdinalIgnoreCase))
                        ||
                        ((cat.Equals("Verification", StringComparison.OrdinalIgnoreCase) || cat.Equals("Verifications", StringComparison.OrdinalIgnoreCase)) &&
                         (sub.Equals("Driver’s License", StringComparison.OrdinalIgnoreCase) ||
                          sub.Equals("Driver's License", StringComparison.OrdinalIgnoreCase) ||
                          sub.Equals("Proof of Address", StringComparison.OrdinalIgnoreCase)));

                    if (ok)
                    {
                        match = true;

                        var cRef = d.GetAttributeValue<EntityReference>(FLD_DOC_Contact);
                        var who = (cRef != null && cRef.Id != Guid.Empty) ? (TryGetContactFullName(svc, tracing, cRef.Id) ?? cRef.Id.ToString()) : "N/A";
                        matchedInfo = $"{cat} / {sub} (Verified) - Contact: {who}";
                        break;
                    }
                }

                tokens[TOKEN_ProofResidencyProvidedLocal] = match;
                facts["rule5.docs.hasVerifiedResidencyDoc"] = match;
                facts["rule5.docs.matchedDocInfo"] = matchedInfo;

                tracing.Trace($"[Rule5] proofresidencyprovided={match}; matchedDoc='{matchedInfo}'");
            }
            catch (Exception ex)
            {
                tracing.Trace("[Rule5] PopulateRule5Tokens_ProofOfResidency failed: " + ex);
                tokens["proofresidencyprovided"] = false;
                facts["rule5.docs.hasVerifiedResidencyDoc"] = false;
                facts["rule5.docs.matchedDocInfo"] = "";
            }
        }

        private static void PopulateRule6Tokens_MostRecentIncomeTaxReturn(
    IOrganizationService svc,
    ITracingService tracing,
    Guid caseId,
    Dictionary<string, object> tokens,
    Dictionary<string, object> facts)
        {
            try
            {
                // Token used in your rule JSON
                const string TOKEN_Local = "mostrecenttaxreturnprovided";

                // We only need "attached" docs (not verified requirement as per Rule 6 text)
                // Category/SubCategory are TEXT fields
                var allowedCategory = "Income";

                var allowedSubCats = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "W-2",
            "Tax Returns",
            "Paystub",
            "Bank Statement",
            "Pay Stubs",        // safety: if someone saved label differently
            "Paystub(s)"        // safety
        };

                var qe = new QueryExpression(ENT_UploadDocument)
                {
                    ColumnSet = new ColumnSet(FLD_DOC_Category, FLD_DOC_SubCategory, FLD_DOC_Contact, "createdon"),
                    Criteria = new FilterExpression(LogicalOperator.And),
                    TopCount = 200
                };

                qe.Criteria.AddCondition(FLD_DOC_Case, ConditionOperator.Equal, caseId);
                qe.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
                qe.Criteria.AddCondition(FLD_DOC_Category, ConditionOperator.Equal, allowedCategory);

                // subcategory OR filter
                var subOr = new FilterExpression(LogicalOperator.Or);
                foreach (var s in allowedSubCats)
                    subOr.AddCondition(FLD_DOC_SubCategory, ConditionOperator.Equal, s);
                qe.Criteria.AddFilter(subOr);

                // latest first (nice for facts)
                qe.Orders.Add(new OrderExpression("createdon", OrderType.Descending));

                var docs = svc.RetrieveMultiple(qe).Entities;
                var match = docs.FirstOrDefault();

                bool pass = (match != null);

                tokens[TOKEN_Local] = pass;

                // facts for UI/debug
                facts["rule6.docs.countMatched"] = docs.Count;
                if (pass)
                {
                    var cat = (match.GetAttributeValue<string>(FLD_DOC_Category) ?? "").Trim();
                    var sub = (match.GetAttributeValue<string>(FLD_DOC_SubCategory) ?? "").Trim();

                    var cRef = match.GetAttributeValue<EntityReference>(FLD_DOC_Contact);
                    var who = (cRef != null && cRef.Id != Guid.Empty)
                        ? (TryGetContactFullName(svc, tracing, cRef.Id) ?? cRef.Id.ToString())
                        : "N/A";

                    facts["rule6.docs.matchedDocInfo"] = $"{cat} / {sub} - Contact: {who}";
                }
                else
                {
                    facts["rule6.docs.matchedDocInfo"] = "";
                }

                tracing.Trace($"[Rule6] mostrecenttaxreturnprovided={pass}; matchedRows={docs.Count}; info='{facts["rule6.docs.matchedDocInfo"]}'");
            }
            catch (Exception ex)
            {
                tracing.Trace("[Rule6] PopulateRule6Tokens_MostRecentIncomeTaxReturn failed: " + ex);
                tokens["mostrecenttaxreturnprovided"] = false;
                facts["rule6.docs.countMatched"] = 0;
                facts["rule6.docs.matchedDocInfo"] = "";
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

        #region ====== Rule 7 token population (State CCS Eibility) ======
        //Rule 7 token population (State CCS Eibility)
        private static void PopulateRule7Tokens(IPluginExecutionContext context, IOrganizationService svc, ITracingService tracing, Guid caseId, Dictionary<string, object> tokens)
        {
            tracing.Trace($"PopulateRule2Tokens Method is called");
            tokens["yearlyincome"] = YearlyHouseHoldIncome(svc, tracing, caseId);
            tokens["householdsizeadjusted"] = CountHouseHoldSize(svc, tracing, caseId);
            tokens["incomewithinrange"] = HasCheckEligibleIncomeRange(context, svc, tracing, caseId);
            //tokens["incomebelowminc"] = HasCheckBelowMinIncome(svc, tracing, caseId);

            tracing.Trace($"Rule7 Tokens => yearlyincome={tokens["yearlyincome"]}, householdsizeadjusted={tokens["householdsizeadjusted"]}, , incomewithinrange={tokens["incomewithinrange"]}");
            tracing.Trace($"PopulateRule2Tokens Method is end");
        }

        private static decimal YearlyHouseHoldIncome(IOrganizationService svc, ITracingService tracing, Guid caseId)
        {
            tracing.Trace($"YearlyHouseHoldIncome Method is called");
            var qe = new QueryExpression(ENT_Case)
            {
                ColumnSet = new ColumnSet(FLD_CASE_YearlyHouseholdIncome, "statecode"),
                TopCount = 50
            };

            qe.Criteria.AddCondition(FLD_CASE_IncidentId, ConditionOperator.Equal, caseId);
            qe.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);


            var caseEntity = svc.RetrieveMultiple(qe).Entities.FirstOrDefault();

            if (caseEntity == null || !caseEntity.Contains(FLD_CASE_YearlyHouseholdIncome))
            {
                tracing.Trace("YearlyHouseHoldIncome: No value found, returning 0");
                tracing.Trace($"YearlyHouseHoldIncome Method is end");
                return 0;
            }

            var money = caseEntity.GetAttributeValue<Money>(FLD_CASE_YearlyHouseholdIncome);
            var value = money?.Value ?? 0;

            tracing.Trace($"YearlyHouseHoldIncome(caseId={caseId}) = {value}");
            tracing.Trace($"YearlyHouseHoldIncome Method is end");
            return value;
        }

        private static decimal CountHouseHoldSize(IOrganizationService svc, ITracingService tracing, Guid caseId)
        {
            tracing.Trace($"CountHouseHoldSize Method is called");

            var houseHoldSize = GetActiveHouseholdCount(svc, tracing, caseId);

            var filteredHousehold = houseHoldSize
                    .Where(e =>
                        e.Contains(FLD_CH_DateEntered) &&
                        (
                            !e.Contains(FLD_CH_DateExited)
                            || (
                                e.GetAttributeValue<DateTime>(FLD_CH_DateEntered).Day !=
                                e.GetAttributeValue<DateTime>(FLD_CH_DateExited).Day
                                ||
                                e.GetAttributeValue<DateTime>(FLD_CH_DateEntered).Month !=
                                e.GetAttributeValue<DateTime>(FLD_CH_DateExited).Month
                            )
                        )
                    )
                    .ToList();

            tracing.Trace($"CountHouseHoldSize is: {filteredHousehold.Count} ");
            tracing.Trace($"CountHouseHoldSize Method is end");
            return filteredHousehold.Count;
        }

        private static bool HasCheckEligibleIncomeRange(IPluginExecutionContext context, IOrganizationService svc, ITracingService tracing, Guid caseId)
        {
            tracing.Trace("HasCheckEligibleIncomeRange method is started");

            var yearlyHouseHoldIncome = YearlyHouseHoldIncome(svc, tracing, caseId);
            tracing.Trace($"Yearly House Hold Income = {yearlyHouseHoldIncome}");

            var caseHouseHoldSize = CountHouseHoldSize(svc, tracing, caseId);
            tracing.Trace($"Case Household Size = {caseHouseHoldSize}");

            var bliId = GetGuidFromInput(context, IN_CaseBenefitLineItemId);

            var bli = svc.Retrieve(
                ENT_BenefitLineItem,
                bliId,
                new ColumnSet(FLD_BLI_Benefit)
            );

            var serviceBenefitRef = bli.GetAttributeValue<EntityReference>(FLD_BLI_Benefit);

            if (serviceBenefitRef == null || string.IsNullOrWhiteSpace(serviceBenefitRef.Name))
            {
                tracing.Trace("Service Benefit Name is NULL or empty");
                return false;
            }

            var serviceBenefitName = serviceBenefitRef.Name;
            tracing.Trace($"Service Benefit Name = {serviceBenefitName}");

            //  Get eligibility admin by name
            var eaQuery = new QueryExpression(ENT_EligibilityAdmin)
            {
                ColumnSet = new ColumnSet(FLD_EA_Name),
                TopCount = 1
            };

            eaQuery.Criteria.AddCondition(
                FLD_EA_Name,
                ConditionOperator.Equal,
                serviceBenefitName
            );

            var eligibilityAdmin =
                svc.RetrieveMultiple(eaQuery).Entities.FirstOrDefault();

            if (eligibilityAdmin == null)
            {
                tracing.Trace("No Eligibility Admin record found");
                return false;
            }

            var eligibilityAdminId = eligibilityAdmin.Id;
            tracing.Trace($"Eligibility Admin Id = {eligibilityAdminId}");

            // Get eligibility income range 
            var rangeQuery = new QueryExpression(ENT_EligibilityIncomeRange)
            {
                ColumnSet = new ColumnSet(
                    FLD_EIR_HouseHoldSize,
                    FLD_EIR_MinIncome,
                    ENT_SubsidyTableName
                )
            };

            rangeQuery.Criteria.AddCondition(
                FLD_EIR_EligibilityAdmin,
                ConditionOperator.Equal,
                eligibilityAdminId
            );

            rangeQuery.Criteria.AddCondition(
             ENT_SubsidyTableName,
            ConditionOperator.Equal,
            "c"
            );

            var ranges = svc.RetrieveMultiple(rangeQuery).Entities;

            if (!ranges.Any())
            {
                tracing.Trace("No Eligibility Income Range records found");
                return false;
            }

            tracing.Trace($"Total Income Range Records = {ranges.Count}");

            // Match household size & income
            var matchedRanges = ranges
                .Where(r =>
                    r.Contains(FLD_EIR_HouseHoldSize) &&
                    r.GetAttributeValue<int>(FLD_EIR_HouseHoldSize) == caseHouseHoldSize &&
                    string.Equals(r.GetAttributeValue<string>(ENT_SubsidyTableName)?.Trim() ?? "", "c",
                     StringComparison.OrdinalIgnoreCase
                     )
                )
                .ToList();

            if (!matchedRanges.Any())
            {
                tracing.Trace("No income range matched for Household Size");
                return false;
            }

            tracing.Trace($"Matched income range count = {matchedRanges.Count}");

            foreach (var range in matchedRanges)
            {
                var minIncomeMoney =
                    range.GetAttributeValue<Money>(FLD_EIR_MinIncome);

                var minIncome = minIncomeMoney?.Value ?? 0;

                tracing.Trace($"Comparing yearlyHouseHoldIncome={yearlyHouseHoldIncome} with MinIncome={minIncome}");
                // not eligible
                if (yearlyHouseHoldIncome >= minIncome)
                {
                    tracing.Trace(
                        "Yearly Eligible Income >= MinIncome : NOT ELIGIBLE");
                    return false;
                }
            }
            bool incomePayStubPresent = HasDocumentByCategorySubcategory(svc, tracing, caseId, null, DocumentCategory.Income, DocumentSubCategory.Paystub);
            bool incomeW2FPresent = HasDocumentByCategorySubcategory(svc, tracing, caseId, null, DocumentCategory.Income, DocumentSubCategory.W2);
            var finalResult = incomePayStubPresent && incomeW2FPresent;
            tracing.Trace($"incomePayStubPresent = {incomePayStubPresent}");
            tracing.Trace($"incomeW2FPresent = {incomeW2FPresent}");
            // eligible
            tracing.Trace("Yearly Eligible Income < MinIncome : ELIGIBLE");
            tracing.Trace("HasCheckEligibleIncomeRange method is end");

            return finalResult;
        }

        #endregion

        #region ====== Rule 8 token population (Child support, court ordered or voluntary child support) ======
        private static void PopulateRule8Tokens(IPluginExecutionContext context, IOrganizationService svc, ITracingService tracing, Guid caseId, Dictionary<string, object> tokens)
        {
            tracing.Trace($"PopulateRule8Tokens Method is called");
            //tokens["pursuingchildsupportorgoodcause"] = ValidateChildSupport(svc, tracing, caseId);
            tokens["ncpexists"] = CheckNcpExistsForChild(svc, tracing, caseId);
            tokens["ncppayschildsupport"] = ValidateChildSupport(svc, tracing, caseId);

            tokens["ncpexists"] = CheckNcpExistsForChild(svc, tracing, caseId);
            tokens["otheradultpartnerorspouse"] = GetActiveHouseholdCount(svc, tracing, caseId, CaseRelationShipLookup.SpouseOrPartner).Any();

            tokens["ncpexists"] = CheckNcpExistsForChild(svc, tracing, caseId);
            tokens["issingleparent"] = CheckMaritalStatusAllowedFromCaseContact(
              svc,
              tracing,
              caseId,
              ContactMaritalStatus.SingleOrNeverMarried,
              ContactMaritalStatus.Divorced,
              ContactMaritalStatus.Separated
            );

            tracing.Trace($"Rule8 Tokens completed");
            tracing.Trace($"PopulateRule8Tokens Method is end");
        }

        private static bool CheckNcpExistsForChild(IOrganizationService svc, ITracingService tracing, Guid caseId)
        {
            tracing.Trace("CheckNcpExistsForChild check started");
            var caseRelationship = new List<InvolvedPartiesRelationship>
                {
                    InvolvedPartiesRelationship.SpouseOrPartner,
                    InvolvedPartiesRelationship.OtherParent,
                    InvolvedPartiesRelationship.Parent,
                    InvolvedPartiesRelationship.OtherFamilyMember
                };
            var caseInvolvedPartRecord = CheckCaseInvolvedParties(svc, tracing, caseId, caseRelationship.ToArray());
            bool isInvolvedPartiesPresent = caseInvolvedPartRecord.Any();

            var houseHoldNcpRecord = GetHouseHoldNcpMembers(svc, tracing, caseId, CaseRelationShipLookup.SpouseOrPartner);
            bool isCaseHouseHoldNcpPresent = houseHoldNcpRecord.Any();

            tracing.Trace($"caseInvolvedPartCheck {isInvolvedPartiesPresent}");
            tracing.Trace($"isActiveCaseHouseHoldPresent {isCaseHouseHoldNcpPresent}");

            tracing.Trace("CheckNcpExistsForChild check ended");
            return isInvolvedPartiesPresent || isCaseHouseHoldNcpPresent;
        }
        private static bool ValidateChildSupport(IOrganizationService svc, ITracingService tracing, Guid caseId)
        {
            bool isChildCaseIncomePresent = HasAnyCaseIncome(svc, tracing, caseId, new[] { CaseIncomeCatergory.Other }, new[] { CaseIncomeSubCatergory.ChildSupport });
            tracing.Trace($"isChildCaseIncomePresent{isChildCaseIncomePresent}");
            bool hasChildSupportDocPresent = HasDocumentByCategorySubcategory(svc, tracing, caseId, null, DocumentCategory.Expenses, DocumentSubCategory.ChildSupport);
            tracing.Trace($"hasChildSupportDocPresent{hasChildSupportDocPresent}");
            return hasChildSupportDocPresent && isChildCaseIncomePresent;
        }
        #endregion

        #region ====== Rule 9 token population (Medical Expense calculation) ======
        //Rule 9 token population (Medical Expense >= 2500)
        private static void PopulateRule9Tokens(IPluginExecutionContext context, IOrganizationService svc, ITracingService tracing, Guid caseId, Dictionary<string, object> tokens)
        {
            tracing.Trace($"PopulateRule9Tokens Method is called");
            tokens["medicalbillsamount"] = CalculateMedicalExpense(svc, tracing, caseId);
            tokens["medicalbillexists"] = HasDocumentByCategorySubcategory(svc, tracing, caseId, null, DocumentCategory.Expenses, DocumentSubCategory.Expense);

            tracing.Trace($"Rule9 Tokens => medicalbillsamount={tokens["medicalbillsamount"]}");
            tracing.Trace($"PopulateRule9Tokens Method is end");
        }

        private static decimal CalculateMedicalExpense(IOrganizationService svc, ITracingService tracing, Guid caseId)
        {
            tracing.Trace("CalculateMedicalExpense method started");

            decimal totalAmount = 0;

            var qe = new QueryExpression(ENT_CaseExpense)
            {
                ColumnSet = new ColumnSet(FLD_CE_ExpenseType, FLD_CE_Amount),
                Criteria = new FilterExpression(LogicalOperator.And)
                {
                    Conditions =
            {
                new ConditionExpression(FLD_Common_Case, ConditionOperator.Equal, caseId),
                new ConditionExpression("statecode", ConditionOperator.Equal, 0),
                new ConditionExpression(
                    FLD_CE_ExpenseType,
                    ConditionOperator.In,
                    new object[]
                    {
                        (int)CaseExpenseType.MedicalBills,
                        (int)CaseExpenseType.MedicalPremiumExcludingMedicare,
                        (int)CaseExpenseType.MedicarePremium
                    })
            }
                }
            };

            var expenses = svc.RetrieveMultiple(qe)?.Entities;

            if (expenses == null || expenses.Count == 0)
            {
                tracing.Trace("No medical expense records found");
                return 0;
            }

            foreach (var expense in expenses)
            {
                var money = expense.GetAttributeValue<Money>(FLD_CE_Amount);
                if (money != null)
                {
                    totalAmount += money.Value;
                }
            }
            ;

            tracing.Trace($"Total Medical Expense (caseId={caseId}) = {totalAmount}");
            tracing.Trace("CalculateMedicalExpense method ended");

            return totalAmount;
        }
        #endregion

        #region ====== Rule 10 token population(Medical Expense calculation) ======
        //Rule 10 token population (single-parent household or has an absent parent)
        private static void PopulateRule10Tokens(IPluginExecutionContext context, IOrganizationService svc, ITracingService tracing, Guid caseId, Dictionary<string, object> tokens)
        {
            tracing.Trace($"PopulateRule10Tokens Method is called");
            tokens["singleparentfamily"] = CheckHouseHoldPartnerAndMaritalStatus(svc, tracing, caseId);
            tokens["childsupportdocumentprovided"] = HasDocumentByCategorySubcategory(svc, tracing, caseId, null, DocumentCategory.Expenses, DocumentSubCategory.ChildSupport);

            tracing.Trace($"Rule10 Tokens => singleparentfamily={tokens["singleparentfamily"]},childsupportdocumentprovided={tokens["childsupportdocumentprovided"]}");
            tracing.Trace($"PopulateRule10Tokens Method is end");
        }


        private static bool CheckHouseHoldPartnerAndMaritalStatus(IOrganizationService svc, ITracingService tracing, Guid caseId)
        {
            tracing.Trace("CheckHouseHoldPartnerAndMaritalStatus check started");

            bool hasHouseholdPartner = GetActiveHouseholdCount(
                svc,
                tracing,
                caseId,
                CaseRelationShipLookup.SpouseOrPartner,
                CaseRelationShipLookup.DomesticPartner
            ).Any();

            tracing.Trace($"Household partner exists: {hasHouseholdPartner}");

            bool validateMaritalStatus = CheckMaritalStatusAllowedFromCaseContact(
              svc,
              tracing,
              caseId,
              ContactMaritalStatus.Single,
              ContactMaritalStatus.Divorced,
              ContactMaritalStatus.Separated
          );

            // returning a single bool value
            bool result = hasHouseholdPartner && validateMaritalStatus;

            tracing.Trace($"CheckHouseHoldPartnerAndMaritalStatus final result: {result}");
            tracing.Trace("CheckHouseHoldPartnerAndMaritalStatus method end");
            return result;
        }


        #endregion

        #region ====== Household ======
        private static List<Entity> GetActiveHouseholdCount(IOrganizationService svc, ITracingService tracing, Guid caseId, params string[] relationships)
        {
            tracing.Trace($"GetActiveHouseholdCount Method is called");
            var qe = new QueryExpression(ENT_CaseHousehold)
            {
                ColumnSet = new ColumnSet(
                    FLD_CH_Contact, FLD_CH_DateEntered, FLD_CH_DateExited, FLD_CH_Primary, FLD_CH_StateCode, FLD_CH_RelationshipRole
                )
            };

            qe.Criteria.AddCondition(FLD_CH_Case, ConditionOperator.Equal, caseId);
            qe.Criteria.AddCondition(FLD_CH_StateCode, ConditionOperator.Equal, 0);
            //qe.Criteria.AddCondition(FLD_CH_DateExited, ConditionOperator.Null);

            if (relationships != null && relationships.Length > 0)
            {
                var roleLink = qe.AddLink(
                    ENT_RelationshipRole,     // target table
                    FLD_CH_RelationshipRole,     // lookup field in CaseHousehold
                    FLD_RR_RRID,   // PK of target
                    JoinOperator.Inner
                );

                roleLink.EntityAlias = "rr";

                roleLink.LinkCriteria.AddCondition(
                    FLD_RR_Name,
                    ConditionOperator.In,
                    relationships.Cast<object>().ToArray()
                );
            }

            var results = svc.RetrieveMultiple(qe).Entities.ToList();
            tracing.Trace($"GetActiveHouseholdCount count: {results.Count}");
            tracing.Trace($"GetActiveHouseholdCount Method is end");
            return results;
        }

        //Helper method for Household NCP check
        private static List<Entity> GetHouseHoldNcpMembers(IOrganizationService svc, ITracingService tracing, Guid caseId, params string[] relationships)
        {
            tracing.Trace($"GetHouseHoldNcpMembers Method is called");
            var qe = new QueryExpression(ENT_CaseHousehold)
            {
                ColumnSet = new ColumnSet(
                    FLD_CH_Contact, FLD_CH_DateEntered, FLD_CH_DateExited, FLD_CH_Primary, FLD_CH_StateCode, FLD_CH_RelationshipRole
                )
            };

            qe.Criteria.AddCondition(FLD_CH_Case, ConditionOperator.Equal, caseId);
            qe.Criteria.AddCondition(FLD_CH_StateCode, ConditionOperator.Equal, 0);
            qe.Criteria.AddCondition(FLD_CH_DateExited, ConditionOperator.NotNull);

            if (relationships != null && relationships.Length > 0)
            {
                var roleLink = qe.AddLink(
                    ENT_RelationshipRole,     // target table
                    FLD_CH_RelationshipRole,     // lookup field in CaseHousehold
                    FLD_RR_RRID,   // PK of target
                    JoinOperator.Inner
                );

                roleLink.EntityAlias = "rr";

                roleLink.LinkCriteria.AddCondition(
                    FLD_RR_Name,
                    ConditionOperator.In,
                    relationships.Cast<object>().ToArray()
                );
            }

            var results = svc.RetrieveMultiple(qe).Entities;

            var matchedRecords = results
                .Where(e =>
                    e.Contains(FLD_CH_DateEntered) &&
                    e.Contains(FLD_CH_DateExited) &&
                    e.GetAttributeValue<DateTime>(FLD_CH_DateEntered).Day ==
                    e.GetAttributeValue<DateTime>(FLD_CH_DateExited).Day &&
                    e.GetAttributeValue<DateTime>(FLD_CH_DateEntered).Month ==
                    e.GetAttributeValue<DateTime>(FLD_CH_DateExited).Month
                )
                .ToList();

            tracing.Trace($"GetHouseHoldNcpMembers count: {results.Count}");
            tracing.Trace($"GetHouseHoldNcpMembers Method is end");
            return matchedRecords;
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
                case "ncpexists": return "NCP Exists?";
                case "ncppayschildsupport": return "NCP Pay for Child Support?";
                case "otheradultpartnerorspouse": return "Any Adult Present?";
                case "issingleparent": return "Is a Single Parent?";
                case "medicalbillexists": return "Medical Bill Exists?";
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
