﻿// Copyright 2014 The Rector & Visitors of the University of Virginia
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Facebook.CoreKit;
using Facebook.LoginKit;
using Foundation;
using SensusService;
using SensusService.Exceptions;
using SensusService.Probes.Apps;
using Xamarin.Forms;

namespace Sensus.iOS.Probes.Apps
{
    public class iOSFacebookProbe : FacebookProbe
    {
        private LoginManager _loginManager;
        private LoginManagerRequestTokenHandler _loginResultHandler;

        private bool HasValidAccessToken
        {
            get { return AccessToken.CurrentAccessToken != null && AccessToken.CurrentAccessToken.ExpirationDate.SecondsSinceReferenceDate >= NSDate.Now.SecondsSinceReferenceDate; }
        }

        public iOSFacebookProbe()
        {
            _loginManager = new LoginManager();
            _loginResultHandler = new LoginManagerRequestTokenHandler((loginResult, error) =>
                {
                    if (error == null && loginResult.Token != null)
                        SensusServiceHelper.Get().Logger.Log("Facebook login succeeded.", SensusService.LoggingLevel.Normal, GetType());
                    else if (loginResult != null && loginResult.IsCancelled)
                        SensusServiceHelper.Get().Logger.Log("Facebook login cancelled.", SensusService.LoggingLevel.Normal, GetType());
                    else
                        SensusServiceHelper.Get().Logger.Log("Facebook login failed.", SensusService.LoggingLevel.Normal, GetType());
                });
        }

        protected override void Initialize()
        {
            base.Initialize();

            if (HasValidAccessToken)
            {
                SensusServiceHelper.Get().Logger.Log("Already have valid Facebook access token. No need to initialize.", LoggingLevel.Normal, GetType());
                return;
            }                

            try
            {
                _loginManager.LogInWithReadPermissions(GetRequiredPermissionNames(), _loginResultHandler);
            }
            catch (Exception ex)
            {
                SensusServiceHelper.Get().Logger.Log("Error while initializing Facebook SDK and/or logging in:  " + ex.Message, LoggingLevel.Normal, GetType());
            }
        }

        protected override IEnumerable<Datum> Poll(CancellationToken cancellationToken)
        {
            List<Datum> data = new List<Datum>();

            if (HasValidAccessToken)
            {
                // prompt user for any missing permissions
                string[] missingPermissions = GetRequiredPermissionNames().Where(p => !AccessToken.CurrentAccessToken.Permissions.Contains(p)).ToArray();
                if (missingPermissions.Length > 0)
                {
                    _loginManager.LogInWithReadPermissions(missingPermissions, _loginResultHandler);
                    return data;
                }

                ManualResetEvent startWait = new ManualResetEvent(false);
                List<ManualResetEvent> responseWaits = new List<ManualResetEvent>();

                Xamarin.Forms.Device.BeginInvokeOnMainThread(() =>
                    {
                        try
                        {
                            GraphRequestConnection requestConnection = new GraphRequestConnection();
                
                            #region add requests to connection
                            foreach (Tuple<string, List<string>> edgeFieldQuery in GetEdgeFieldQueries())
                            {
                                NSDictionary parameters = null;
                                if (edgeFieldQuery.Item2.Count > 0)
                                    parameters = new NSDictionary("fields", string.Concat(edgeFieldQuery.Item2.Select(field => field + ",")).Trim(','));

                                GraphRequest request = new GraphRequest(
                                                           "me" + (edgeFieldQuery.Item1 == null ? "" : "/" + edgeFieldQuery.Item1),
                                                           parameters,
                                                           AccessToken.CurrentAccessToken.TokenString,
                                                           null,
                                                           "GET");
                            
                                ManualResetEvent responseWait = new ManualResetEvent(false);

                                requestConnection.AddRequest(request, (connection, result, error) =>
                                    {
                                        if (error == null)
                                        {
                                            FacebookDatum datum = new FacebookDatum(DateTimeOffset.UtcNow);

                                            #region set datum properties
                                            NSDictionary resultDictionary = result as NSDictionary;
                                            bool valuesSet = false;
                                            foreach (string resultKey in resultDictionary.Keys.Select(k => k.ToString()))
                                            {
                                                PropertyInfo property;
                                                if (FacebookDatum.TryGetProperty(resultKey, out property))
                                                {
                                                    object value = null;

                                                    if (property.PropertyType == typeof(string))
                                                        value = resultDictionary[resultKey].ToString();
                                                    else if (property.PropertyType == typeof(bool))
                                                    {
                                                        int parsedBool;
                                                        if (int.TryParse(resultDictionary[resultKey].ToString(), out parsedBool))
                                                            value = parsedBool == 1 ? true : false;
                                                    }
                                                    else if (property.PropertyType == typeof(DateTimeOffset))
                                                    {
                                                        DateTimeOffset parsedDateTimeOffset;
                                                        if (DateTimeOffset.TryParse(resultDictionary[resultKey].ToString(), out parsedDateTimeOffset))
                                                            value = parsedDateTimeOffset;
                                                    }
                                                    else if (property.PropertyType == typeof(List<string>))
                                                    {
                                                        List<string> values = new List<string>();

                                                        NSArray resultArray = resultDictionary[resultKey] as NSArray;
                                                        for (nuint i = 0; i < resultArray.Count; ++i)
                                                            values.Add(resultArray.GetItem<NSObject>(i).ToString());

                                                        value = values;
                                                    }
                                                    else
                                                        throw new SensusException("Unrecognized FacebookDatum property type:  " + property.PropertyType.ToString());

                                                    if (value != null)
                                                    {
                                                        property.SetValue(datum, value);
                                                        valuesSet = true;
                                                    }
                                                }
                                                else
                                                    SensusServiceHelper.Get().Logger.Log("Unrecognized key in Facebook result dictionary:  " + resultKey, LoggingLevel.Verbose, GetType());
                                            }
                                            #endregion

                                            if (valuesSet)
                                                data.Add(datum);
                                        }
                                        else
                                            SensusServiceHelper.Get().Logger.Log("Error received while querying Facebook graph API:  " + error.Description, LoggingLevel.Normal, GetType());

                                        SensusServiceHelper.Get().Logger.Log("Response for \"" + request.GraphPath + "\" has been processed.", LoggingLevel.Verbose, GetType());
                                        responseWait.Set();
                                    });

                                responseWaits.Add(responseWait);
                            }
                            #endregion

                            if (responseWaits.Count == 0)
                                SensusServiceHelper.Get().Logger.Log("Request connection contained zero requests.", LoggingLevel.Normal, GetType());
                            else
                            {
                                SensusServiceHelper.Get().Logger.Log("Starting request connection with " + responseWaits.Count + " requests.", LoggingLevel.Normal, GetType());                    
                                requestConnection.Start();
                            }

                            startWait.Set();
                        }
                        catch (Exception ex)
                        {
                            SensusServiceHelper.Get().Logger.Log("Error starting request connection:  " + ex.Message, LoggingLevel.Normal, GetType());
                        }
                    });

                startWait.WaitOne();

                // wait for all responses to be processed
                foreach (ManualResetEvent responseWait in responseWaits)
                    responseWait.WaitOne();
            }
            else
                SensusServiceHelper.Get().Logger.Log("Attempted to poll Facebook probe without a valid access token.", LoggingLevel.Normal, GetType());

            return data;
        }
       
        public override bool TestHealth(ref string error, ref string warning, ref string misc)
        {
            bool restart = base.TestHealth(ref error, ref warning, ref misc);

            if (!HasValidAccessToken)
                restart = true;

            return restart;
        }

        protected override ICollection<string> GetGrantedPermissions()
        {
            if (HasValidAccessToken)
            {
                List<string> permissions = new List<string>();
                foreach (NSString permission in AccessToken.CurrentAccessToken.Permissions)
                    permissions.Add(permission.ToString());

                return permissions;
            }
            else
                return new string[0];
        }
    }
}