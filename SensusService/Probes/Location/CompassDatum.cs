// Copyright 2014 The Rector & Visitors of the University of Virginia
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

using Newtonsoft.Json;
using SensusService.Probes.User.ProbeTriggerProperties;
using System;
using SensusService.Anonymization;
using SensusService.Anonymization.Anonymizers;

namespace SensusService.Probes.Location
{
    public class CompassDatum : Datum
    {
        private double _heading;

        [NumberProbeTriggerProperty]
        [Anonymizable(null, new Type[] { typeof(DoubleRoundingOnesAnonymizer), typeof(DoubleRoundingTensAnonymizer), typeof(DoubleRoundingHundredsAnonymizer) }, -1)]
        public double Heading
        {
            get { return _heading; }
            set { _heading = value; }
        }

        [JsonIgnore]
        public override string DisplayDetail
        {
            get { return Math.Round(_heading, 0) + " degrees from magnetic north"; }
        }

        /// <summary>
        /// For JSON deserialization.
        /// </summary>
        private CompassDatum() { }

        public CompassDatum(DateTimeOffset timestamp, double heading)
            : base(timestamp)
        {
            _heading = heading;
        }

        public override string ToString()
        {
            return base.ToString() + Environment.NewLine +
                   "Heading:  " + _heading;
        }
    }
}
