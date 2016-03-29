﻿using System;
using System.Collections.Generic;
using System.IO;
using com.softwarekey.Client.Gui;
using com.softwarekey.Client.Licensing;
using com.softwarekey.Client.WebService.XmlLicenseFileService;

namespace com.softwarekey.Client.Sample
{
    /// <summary>Example writable license implementation, which uses licenses issued, encrypted, and digitally signed by SOLO Server or License Manager, or licenses generated by the protected application.</summary>
    /// <remarks>
    /// <note type="caution">
    /// <para>IMPORTANT: his code depends on the data from the LicenseConfiguration class.  Make sure you update the members in that class if you copy it
    /// into your application! This is necessary to ensure your application is properly secured, and prevents your application from being
    /// activated with licenses generated for other applications.</para>
    /// </note>
    /// </remarks>
    internal partial class SampleLicense : WritableLicense
    {
        //TODO: IMPORTANT: This code depends on the data from the LicenseConfiguration class.  Make sure you update the members in that class if you copy it into your application!
        #region Constructors
        /// <summary>Creates a new <see cref="SampleLicense"/>.</summary>
        /// <param name="settings"><see cref="LicensingGui"/> object used, which can provide objects used for making web service calls.</param>
        internal SampleLicense(LicensingGui settings)
            : base(LicenseConfiguration.EncryptionKey, true, true, LicenseConfiguration.ThisProductID, LicenseConfiguration.ThisProductVersion, LicenseConfiguration.SystemIdentifierAlgorithms)
        {
            m_Settings = settings;

            //Initialize the alias configuration...
            foreach (LicenseAlias alias in LicenseConfiguration.Aliases)
                AddAlias(alias);
        }
        #endregion

        #region Internal Properties
        /// <summary>The date the downloadable license was last validated.</summary>
        internal DateTime DateDownloadableLicenseValidated
        {
            get { return UserDefinedDate1; }
            set { UserDefinedDate1 = value; }
        }

        /// <summary>Gets whether or not this type of license is writable.</summary>
        internal bool IsWritable
        {
            get { return true; }
        }
        #endregion

        #region Internal Methods
        /// <summary>Calculates a new EffectiveEndDate.</summary>
        /// <param name="duration">The number of days the license should last.</param>
        /// <param name="extendExisting">If true, the existing, non-expired license will be extended by the number of days specified in the duration argument.</param>
        /// <returns>The new EffectiveEndDate</returns>
        internal DateTime CalculateNewEffectiveEndDate(int duration, bool extendExisting)
        {
            if (duration > 0 && extendExisting)
            {
                int currentDaysLeft = (int)EffectiveEndDate.Subtract(DateTime.UtcNow.Date).TotalDays;
                if (currentDaysLeft > 0)
                    duration += currentDaysLeft;
            }

            return DateTime.UtcNow.Date.AddDays(duration);
        }

        /// <summary>Removes activation details.</summary>
        /// <remarks>This method is called when the license type is changing.</remarks>
        internal void ClearActivationDetails()
        {
            LicenseID = 0;
            InstallationID = "";
            InstallationName = "";
        }

        /// <summary>Creates an expired evaluation license.</summary>
        /// <returns>Returns true if the expired evaluation license file was created successfully.  If false is returned, check the <see cref="License.LastError"/> property for details.</returns>
        internal bool CreateExpiredEvaluation()
        {
            return CreateEvaluation(-1, false, false);
        }

        /// <summary>Creates a fresh trial license and returns true if successful</summary>
        /// <returns>Returns true if the fresh evaluation license file was created successfully.  If false is returned, check the <see cref="License.LastError"/> property for details.</returns>
        internal bool CreateFreshEvaluation()
        {
            return CreateEvaluation(LicenseConfiguration.FreshEvaluationDuration, true, false);
        }

        /// <summary>Creates an evaluation for the specified duration.</summary>
        /// <param name="evaluationDuration">The duration (in days) of the evaluation.</param>
        /// <param name="shouldCheckAliases">Whether or not aliases should be checked before creating the new evaluation license.</param>
        /// <param name="extendExisting">Whether or not any existing trial period should be extended.</param>
        /// <returns>Returns true if successful.  If false is returned, check the <see cref="License.LastError"/> property for details.</returns>
        internal bool CreateEvaluation(int evaluationDuration, bool shouldCheckAliases, bool extendExisting)
        {
            if (shouldCheckAliases)
            {
                //Start by loading and checking all the aliases
                int numAliases, numValidAliases;
                this.CheckAliases(out numAliases, out numValidAliases);

                //If we found any aliases, write the most recent one as the license file
                LicenseAlias mostRecent = LicenseAlias.GetMostCurrentAlias(this.Aliases);
                if (mostRecent.LastUpdated != DateTime.MinValue)
                {
                    this.WriteAliasToLicenseFile(mostRecent, LicenseConfiguration.LicenseFilePath);
                    this.Load(mostRecent.Contents);
                    int aliasesToWrite, aliasesWritten;
                    this.WriteAliases(out aliasesToWrite, out aliasesWritten);
                    return true;
                }
            }

            //This sample uses the TriggerCode property to determine the type of license issued, so set it accordingly.
            TriggerCode = (int)LicenseTypes.Unlicensed;

            //Set the Product ID so this evaluation license cannot be used to update or extend another application's evaluation period.
            Product.ProductID = ThisProductID;

            //Evaluations that are not established through activation should have no License ID, Installation ID, or Installation Name.
            ClearActivationDetails();

            //TODO: IMPORTANT: Add code to clear-out other license data that is not applicable to evaluations of your license.
            //                 For example, the code below clears the date in which any downloadable license was validated, which
            //                 is stored in the UserDefinedDate1 field for this sample.
            DateDownloadableLicenseValidated = DateTime.MinValue;

            //Creating an evaluation should also remove any prior volume license data.
            RemoveVolumeLicense();

            if (evaluationDuration > 0)
            {
                //We are creating an evaluation that expires in the future, so set the effective start date to today's date.
                this.EffectiveStartDate = DateTime.UtcNow.Date;
            }
            else
            {
                //If we get into this code block, then we are creating an expired evaluation, and we should just make the start date the same as the end date.
                this.EffectiveStartDate = DateTime.UtcNow.Date.AddDays(evaluationDuration);
            }

            //Now set the evaluation's expiration date.
            this.EffectiveEndDate = CalculateNewEffectiveEndDate(evaluationDuration, extendExisting);

            //Write the aliases.
            int filesToWrite, filesWritten;
            this.WriteAliases(out filesToWrite, out filesWritten);

            //TODO: you can add your own logic here to set your own requirements for how many aliases must be written
            //      ...for this example, we only require 1
            if (filesWritten < 1)
            {
                return false;
            }

            //Write the new license file.
            return this.WriteLicenseFile(LicenseConfiguration.LicenseFilePath);
        }

        /// <summary>Generates a new <see cref="SystemIdentifierValidation"/> object.</summary>
        /// <returns>Returns a new <see cref="SystemIdentifierValidation"/> object.</returns>
        internal SystemIdentifierValidation GenerateIdentifierValidation()
        {
            return new SystemIdentifierValidation(
                AuthorizedIdentifiers,
                CurrentIdentifiers,
                SystemIdentifierValidation.REQUIRE_EXACT_MATCH);
        }

        /// <summary>Loads and initializes the license.</summary>
        /// <returns>Returns true if successful.  If false is returned, check the <see cref="License.LastError"/> property for details.</returns>
        internal bool InitializeLicense()
        {
            if (File.Exists(LicenseConfiguration.VolumeLicenseFilePath))
                return InitializeVolumeLicense();

            //Load the license file.
            bool successful = LoadFile(LicenseConfiguration.LicenseFilePath);

            if (successful)
            {
                //The license was loaded, so run the validation.
                successful = Validate();
            }
            else
            {
                //Validation failed because the license could not even be loaded.
                successful = CreateFreshEvaluation();

                if (successful)
                {
                    //The fresh evaluation was created, so re-load the license file.
                    successful = LoadFile(LicenseConfiguration.LicenseFilePath);

                    if (successful)
                    {
                        //Now validate the evaluation license file.
                        successful = Validate();
                    }
                }
            }

            return successful;
        }

        /// <summary>Initializes the volume license</summary>
        /// <returns>Returns true if successful.  If false is returned, check the <see cref="License.LastError"/> property for details.</returns>
        internal bool InitializeVolumeLicense()
        {
            VolumeLicense vlic = new VolumeLicense();
            
            //First try to load the volume license.
            if (!vlic.LoadVolumeLicenseFile(LicenseConfiguration.VolumeLicenseFilePath))
            {
                LastError = vlic.LastError;
                return false;
            }

            if (!File.Exists(LicenseConfiguration.LicenseFilePath) ||
                !LoadFile(LicenseConfiguration.LicenseFilePath) ||
                (ProductOption.OptionType != LicenseProductOption.ProductOptionType.VolumeLicense &&
                    ProductOption.OptionType != LicenseProductOption.ProductOptionType.DownloadableLicenseWithTriggerCodeValidation) ||
                (ProductOption.OptionType != vlic.ProductOption.OptionType && (
                    ProductOption.OptionType == LicenseProductOption.ProductOptionType.VolumeLicense ||
                    ProductOption.OptionType == LicenseProductOption.ProductOptionType.DownloadableLicenseWithTriggerCodeValidation)))
            {
                //If we reach the code in this if statement, one of the following conditions have been met:
                //    * The initial self-signed/writable license file has not been created yet, and the volume/downloadable license file is present; OR
                //    * A self-signed/writable license file has been created, but for some other kind of license (i.e. an evaluation); OR
                //    * A self-signed/writable license file has been created, but the volume license was swapped out with a downloadable license or vice versa.
                //
                //   Under these conditions, we want to update the writable copy of the license file with the content from the new, volume/downloadable license file.

                //Now try to load the volume license using the self-signed/writable license object.
                if (!LoadFile(LicenseConfiguration.VolumeLicenseFilePath))
                    return false;

                //Now save the self-signed/writable license file with the volume license's data.
                if (!SaveLicenseFile())
                    return false;
            }
            else if ((LicenseConfiguration.DownloadableLicenseOverwriteWithNewerAllowed || LicenseConfiguration.DownloadableLicenseOverwriteWithOlderAllowed) &&
                    vlic.LoadVolumeLicenseFile(LicenseConfiguration.VolumeLicenseFilePath) &&
                    Validate())
            {
                bool isNewer = LicenseConfiguration.DownloadableLicenseOverwriteWithNewerAllowed && vlic.SignatureDate.Subtract(SignatureDate).TotalDays > 0;
                bool isOlder = LicenseConfiguration.DownloadableLicenseOverwriteWithOlderAllowed && vlic.SignatureDate.Subtract(SignatureDate).TotalDays < 0;
                if (isNewer || isOlder)
                {
                    //The volume/downloadable license was loaded, it is newer than the existing self-signed/writable license file, and
                    //the existing self-signed writable license file is valid.

                    //TODO: Store any data that should not be overwritten by the new, downloadable license file here.

                    //Store the date the license was validated so we can restore it after updating the license.
                    DateTime dateValidated = DateDownloadableLicenseValidated;

                    //Now load the new license data from the volume/downloadable license file.
                    if (!LoadFile(LicenseConfiguration.VolumeLicenseFilePath))
                        return false;

                    //If activation is not required, restore the date the license was validated.
                    if ((isNewer && !LicenseConfiguration.DownloadableLicenseOverwriteWithNewerRequiresActivation) ||
                        (isOlder && !LicenseConfiguration.DownloadableLicenseOverwriteWithOlderRequiresActivation))
                    {
                        DateDownloadableLicenseValidated = dateValidated;
                    }

                    //TODO: Restore any data that should not be overwritten by the new, downloadable license file here.

                    //Save the new data.
                    if (!SaveLicenseFile())
                        return false;

                    //Re-load the new license file one more time to make sure we have the right data.
                    if (!LoadFile(LicenseConfiguration.LicenseFilePath))
                        return false;
                }
            }

            //Finally, return the validation result.
            return Validate();
        }

        /// <summary>Removes a license from the system.  This should be done when the license is deactivated or is found to be invalid on SOLO Server.</summary>
        /// <returns>Returns true if it was completed successfully.  If false is returned, check the <see cref="License.LastError"/> property for details.</returns>
        internal bool RemoveLicense()
        {
            return CreateExpiredEvaluation();
        }

        /// <summary>Removes a volume license and any volume license data.</summary>
        internal void RemoveVolumeLicense()
        {
            //Creating an evaluation should also clear out other, pre-existing licensing data...
            DateDownloadableLicenseValidated = DateTime.MinValue;
            ProductOption.OptionType = LicenseProductOption.ProductOptionType.ActivationCode;

            //Try to delete the volume license file if present.
            if (File.Exists(LicenseConfiguration.VolumeLicenseFilePath))
            {
                try
                {
                    File.Delete(LicenseConfiguration.VolumeLicenseFilePath);
                }
                catch (Exception) { /*do nothing if the volume license file could not be deleted -- validation may fail later if this happens */ }
            }
        }

        /// <summary>Saves the license file to the file system.</summary>
        /// <returns>Returns true if successful, false if it failed.</returns>
        internal bool SaveLicenseFile()
        {
            //Now try to write all of the aliases.
            int filesToWrite, filesWritten;
            WriteAliases(out filesToWrite, out filesWritten);

            //Now write the primary license file.
            return WriteLicenseFile(LicenseConfiguration.LicenseFilePath);
        }

        /// <summary>Saves a new license file to the file system.</summary>
        /// <param name="licenseContent">The new license file content to save to disk.</param>
        /// <param name="forceAliasUpdates">Whether or not aliases should be updated even when the system clock appears to have been back-dated.</param>
        /// <returns>Returns true if the license file was saved successfully.  If false is returned, check the <see cref="License.LastError"/> property for details.</returns>
        internal bool SaveLicenseFile(string licenseContent, bool forceAliasUpdates)
        {
            //First load the new license content
            if (!Load(licenseContent))
                return false;

            //If this is a new volume or downloadable license, save the content as such.
            if ((ProductOption.OptionType == LicenseProductOption.ProductOptionType.DownloadableLicenseWithTriggerCodeValidation ||
                    ProductOption.OptionType == LicenseProductOption.ProductOptionType.VolumeLicense) &&
                !SaveVolumeLicenseFile(licenseContent))
            {
                return false;
            }

            //Now try to write all of the aliases.
            int filesToWrite, filesWritten;
            WriteAliases(out filesToWrite, out filesWritten, forceAliasUpdates);

            //TODO: You can make this more permissive/relaxed by only requiring some aliases to be written successfully.
            if (filesWritten != filesToWrite)
            {
                //Return failure if any aliases could not be written.
                return false;
            }

            //Now write the primary license file.
            return WriteLicenseFile(LicenseConfiguration.LicenseFilePath);
        }

        /// <summary>Validates the license.</summary>
        /// <returns>Returns true if validation is successful and the license is valid.  If false is returned, check the <see cref="License.LastError"/> property for details.</returns>
        internal bool Validate()
        {
            //If this is a downloadable license which has not been validated with a trigger code, force validation to fail with the appropriate error.
            if (ProductOption.OptionType == LicenseProductOption.ProductOptionType.DownloadableLicenseWithTriggerCodeValidation &&
                DateTime.MinValue == DateDownloadableLicenseValidated)
            {
                LastError = new LicenseError(LicenseError.ERROR_LICENSE_NOT_EFFECTIVE_YET);
                return false;
            }

            //If this is a downloadable or volume license, validate it differently using the ValidateVolumeLicense method.
            if (ProductOption.OptionType == LicenseProductOption.ProductOptionType.VolumeLicense ||
                ProductOption.OptionType == LicenseProductOption.ProductOptionType.DownloadableLicenseWithTriggerCodeValidation)
            {
                return ValidateVolumeLicense();
            }

            //Validate the aliases.
            LicenseAliasValidation aliasValidation = new LicenseAliasValidation(this);
            bool refreshRequiredForAliases = false;
            LicenseError aliasError = null;
            if (!aliasValidation.Validate())
            {
                if (!LicenseConfiguration.RefreshLicenseEnabled ||
                    string.IsNullOrEmpty(InstallationID) ||
                    TypeOfLicense == LicenseTypes.Unlicensed)
                {
                    //Alias validation failed, and license refreshing is not enabled (meaning we cannot recover via a refresh),
                    //so just return the validation error.
                    LastError = aliasValidation.LastError;
                    return false;
                }

                //Alias validation failed, and license refreshing is enabled, so take note so we can try updating the license
                //via a refresh with SOLO Server.  TODO: If you do not wish to require phoning-home with SOLO Server when alias
                //validation fails, comment-out the two lines below this comment.
                refreshRequiredForAliases = true;
                aliasError = aliasValidation.LastError;
            }

            if (!string.IsNullOrEmpty(InstallationID) &&
                LicenseTypes.Unlicensed != TypeOfLicense &&
                (refreshRequiredForAliases || IsRefreshLicenseAttemptDue))
            {
                //If a refresh attempt should be made, try to perform a license refresh with SOLO Server.
                if (!RefreshLicense() &&
                    (LastError.ErrorNumber != LicenseError.ERROR_WEBSERVICE_CALL_FAILED || refreshRequiredForAliases || IsRefreshLicenseRequired))
                {
                    //The refresh failed and was required, or SOLO Server returned an error.
                    if (aliasError != null)
                        LastError = aliasError;
                    return false;
                }
            }

            //Create a list of validations to perform.
            List<SystemValidation> validations = new List<SystemValidation>();

            //Add a validation to make sure there is no active system clock tampering taking place.
            validations.Add(new SystemClockValidation());

            //Validate the Product ID authorized in the license to make sure the license file was issued for this application.
            validations.Add(new LicenseProductValidation(this, ThisProductID));

            //Add a validation to make sure this system is authorized to use the activated license.  (This implements copy-protection.)
            validations.Add(GenerateIdentifierValidation());

            if (LicenseTypes.TimeLimited == TypeOfLicense ||
                LicenseTypes.Unlicensed == TypeOfLicense)
            {
                //If the license is time-limited or an evaluation, make sure it is within its effective date/time period.
                validations.Add(new LicenseEffectiveDateValidation(this));

                //Validate the system date and time using Network Time Protocol (un-comment the SystemDateTimeValidation below).
                //IMPORTANT: Read the documentation for more details about using NTP!  Do NOT use the NTP servers shown in the
                //           sample code immediately below this comment block.
                //--------------------------------------------------------------------------------------------------------------
                //SystemDateTimeValidation ntpValidation = new SystemDateTimeValidation();
                //ntpValidation.AddTimeServerCheck("time.windows.com");
                //ntpValidation.AddTimeServerCheck("time.nist.gov");
                //validations.Add(ntpValidation);
            }

            //Run all of the validations (in the order they were added), and make sure all of them succeed.
            foreach (SystemValidation validation in validations)
            {
                if (!validation.Validate())
                {
                    LastError = validation.LastError;
                    return false;
                }
            }

            //If we got this far, all validations were successful, so return true to indicate success and a valid license.
            return true;
        }

        /// <summary>Validates a volume license.</summary>
        /// <returns>Returns true if successful.  If false is returned, check the <see cref="License.LastError"/> property for details.</returns>
        internal bool ValidateVolumeLicense()
        {
            VolumeLicense vlic = new VolumeLicense();
            if (!vlic.LoadVolumeLicenseFile(LicenseConfiguration.VolumeLicenseFilePath))
            {
                LastError = vlic.LastError;
                return false;
            }

            if (LicenseID != vlic.LicenseID)
            {
                LastError = new LicenseError(LicenseError.ERROR_LICENSE_SYSTEM_IDENTIFIERS_DONT_MATCH);
                return false;
            }

            if (!vlic.Validate())
            {
                LastError = vlic.LastError;
                return false;
            }

            //TODO: Uncomment the block of code below if you wish to have volume licenses attempt to refresh with SOLO Server.
            //      Note that this can cause the application to experience some delay while the refresh is being attempted,
            //      which is especially noticeable when SOLO Server cannot be reached.
            /*if (!string.IsNullOrEmpty(vlic.InstallationID) &&
                LicenseTypes.Unlicensed != vlic.TypeOfLicense &&
                LicenseMethods.IsRefreshLicenseAttemptDue(vlic.SignatureDate))
            {
                using (XmlLicenseFileService ws = m_Settings.CreateNewXmlLicenseFileServiceObject())
                {
                    //If a refresh attempt should be made, try to perform a license refresh with SOLO Server.
                    if (!vlic.RefreshLicense(ws) &&
                        (vlic.LastError.ErrorNumber != LicenseError.ERROR_WEBSERVICE_CALL_FAILED || LicenseMethods.IsRefreshLicenseRequired(vlic.SignatureDate)))
                    {
                        //The refresh failed and was required, or SOLO Server returned an error.
                        LastError = vlic.LastError;
                        return false;
                    }
                }
            }*/

            //Create a list of validations to perform.
            List<SystemValidation> validations = new List<SystemValidation>();

            //Add a validation to make sure there is no active system clock tampering taking place.
            validations.Add(new SystemClockValidation());

            //Only validate the system identifiers if we activated using trigger codes.  (Ignore this validation if it is a volume license.)
            if (ProductOption.OptionType == LicenseProductOption.ProductOptionType.DownloadableLicenseWithTriggerCodeValidation)
            {
                //Add a validation to make sure this system is authorized to use the activated license.
                validations.Add(new SystemIdentifierValidation(
                    AuthorizedIdentifiers,
                    CurrentIdentifiers,
                    SystemIdentifierValidation.REQUIRE_EXACT_MATCH));
            }

            //Run all of the validations (in the order they were added), and make sure all of them succeed.
            foreach (SystemValidation validation in validations)
            {
                if (!validation.Validate())
                {
                    LastError = validation.LastError;
                    return false;
                }
            }

            return true;
        }

        /// <summary>Processes a Protection PLUS 4 compatible trigger code.</summary>
        /// <param name="licenseID">The License ID entered by the user.</param>
        /// <param name="password">The password entered by the user.</param>
        /// <param name="triggerCodeNumber">The trigger code number to process.</param>
        /// <param name="triggerCodeEventData">The trigger code event data.</param>
        /// <returns>Returns true if the trigger code was processed successfully.</returns>
        internal bool ProcessTriggerCode(int licenseID, string password, int triggerCodeNumber, int triggerCodeEventData)
        {
            bool isValidTriggerCodeNumber = true;
            bool isValidTriggerCodeEventData = true;

            //Save the License ID entered by the user in the license file.
            if (licenseID > 0)
                LicenseID = licenseID;

            switch (triggerCodeNumber)
            {
                case 1:
                case 28: //Activates a full/non-expiring license.
                    //If we are changing the type of license, clear the details for the prior activation so we don't end up
                    //overwriting the new license type after doing a refresh in the future.
                    if (TypeOfLicense != LicenseTypes.FullNonExpiring)
                        ClearActivationDetails();

                    //This sample uses the TriggerCode property to determine the type of license issued, so set it accordingly.
                    TriggerCode = (int)LicenseTypes.FullNonExpiring;

                    //Remove any volume license data
                    RemoveVolumeLicense();

                    //Now try to write all of the aliases and the license file.
                    SaveLicenseFile();

                    break;
                case 10:
                case 11:
                case 29: //Activates a time-limited/periodic license.
                    if (triggerCodeEventData < 1)
                    {
                        isValidTriggerCodeEventData = false;
                        break;
                    }

                    //If we are changing the type of license, clear the details for the prior activation so we don't end up
                    //overwriting the new license type after doing a refresh in the future.
                    if (TypeOfLicense != LicenseTypes.TimeLimited)
                        ClearActivationDetails();

                    //This sample uses the TriggerCode property to determine the type of license issued, so set it accordingly.
                    TriggerCode = (int)LicenseTypes.TimeLimited;

                    //Calculate the new effective end date.  This extends a non-expired licenses from their existing expiration
                    //date when the trigger code number is not 11.  Otherwise, if the trigger code number is 11, the new
                    //expiration date is *always* calculated from the current date.
                    EffectiveEndDate = CalculateNewEffectiveEndDate(triggerCodeEventData, (11 != triggerCodeNumber));
                    
                    //Remove any volume license data
                    RemoveVolumeLicense();

                    
                    SaveLicenseFile();

                    break;
                case 18: //Activates a downloadable license
                    //This sample uses the TriggerCode property to determine the type of license issued, so set it accordingly.
                    TriggerCode = (int)LicenseTypes.FullNonExpiring;

                    //Store the date and time the license was verified.
                    DateDownloadableLicenseValidated = DateTime.UtcNow;

                    //Now try to write all of the aliases and the license file.
                    SaveLicenseFile();

                    break;
                case 20: //Extends an evaluation.
                    if (triggerCodeEventData < 1)
                    {
                        isValidTriggerCodeEventData = false;
                        break;
                    }

                    //Create the new evaluation.
                    CreateEvaluation(triggerCodeEventData, false, true);

                    break;
                default:
                    isValidTriggerCodeNumber = false;
                    break;
            }

            if (!isValidTriggerCodeNumber)
            {
                LastError = new LicenseError(LicenseError.ERROR_TRIGGER_CODE_INVALID);
                return false;
            }

            if (!isValidTriggerCodeEventData)
            {
                LastError = new LicenseError(LicenseError.ERROR_TRIGGER_CODE_EVENT_DATA_INVALID);
                return false;
            }

            return true;
        }

        /// <summary>Called when the application is closing.</summary>
        internal void UnloadLicense()
        {
            //Only allow the aliases and license file to get saved when the current system identifiers are valid.
            SystemIdentifierValidation identifierValidation = GenerateIdentifierValidation();
            if (!identifierValidation.Validate())
                return;

            //Now try to write all of the aliases.
            int filesToWrite, filesWritten;
            WriteAliases(out filesToWrite, out filesWritten);

            //TODO: You can add logic to react to any failure to write an alias.
            //      Since this is called when the application is closing, we
            //      will disregard how many aliases are actually written.

            //Now write the primary license file.
            WriteLicenseFile(LicenseConfiguration.LicenseFilePath);
        }
        #endregion
    }
}
