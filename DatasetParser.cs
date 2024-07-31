using Landis.Core;
using Landis.Utilities;
using Landis.Library.UniversalCohorts;
using System.Text;
using System;
using System.Dynamic;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Data;



namespace Landis.Library.InitialCommunities.Universal
{
    /// <summary>
    /// A parser that reads a dataset of initial communities from text input.
    /// </summary>
    public class DatasetParser
        : TextParser<IDataset>
    {
        private int successionTimestep;
        private ISpeciesDataset speciesDataset;
        private ExpandoObject additionalParameters;
        private string file;
        //private static DataTable CSVCommunityDataTable;


        public override string LandisDataValue
        {
            get
            {
                return "Initial Communities";
            }
        }



        //---------------------------------------------------------------------

        public DatasetParser(int successionTimestep,
                             ISpeciesDataset speciesDataset,
                             ExpandoObject additionalParameters,
                             string file)
        {
            this.successionTimestep = successionTimestep;
            this.speciesDataset = speciesDataset;
            this.additionalParameters = additionalParameters;
            this.file = file;
        }

        //---------------------------------------------------------------------

        /// <summary>
        /// Creates a new InputValueException for an invalid percentage input
        /// value.
        /// </summary>
        /// <returns></returns>
        public static InputValueException MakeInputValueException(string value,
                                                                  string message)
        {
            return new InputValueException(value,
                                           string.Format("\"{0}\" is not a valid aboveground biomass input", value.TrimStart('(')),
                                           new MultiLineText(message));
        }
        //---------------------------------------------------------------------

        protected override IDataset Parse()
        {
            Dataset dataset; // = new Dataset();
            dataset = ReadCSVInputFile(file);

            return dataset;

        }

        //---------------------------------------------------------------------
        private Dataset ReadCSVInputFile(string path)
        {
            string fileExt = System.IO.Path.GetExtension(path);

            if (fileExt != ".csv")
            {
                throw new Exception("Initial Commmunities File: " + path + " is not a CSV. Please provide an Initial Communities File in .csv format.");
            }

            Dataset dataset = new Dataset();
            CSVParser communityParser = new CSVParser();
            DataTable communityTable = communityParser.ParseToDataTable(path);
            Dictionary<int, List<ISpeciesCohorts>> mapCodeList = new Dictionary<int, List<ISpeciesCohorts>>();
            Dictionary<string, bool> trackWarnings = new Dictionary<string, bool>();

            foreach (var parm in this.additionalParameters)
            {
                trackWarnings.Add(parm.Key, false);
            }

            //float initialLeafBiomass = (float)0.0;

            foreach (DataRow row in communityTable.Rows)
            {
                // Read First Record:  MapCode, Spp, Age, WoodBiomass
                int mapCode = System.Convert.ToInt32(row["MapCode"]);
                string speciesName = System.Convert.ToString(row["SpeciesName"]);
                ExpandoObject addParams = new ExpandoObject();

                List<ISpeciesCohorts> listOfCohorts = new List<ISpeciesCohorts>();

                if (speciesName.Trim() == "NA")
                {
                    if (mapCodeList.ContainsKey(mapCode))
                        throw new InputValueException(speciesName, "{0} cannot be NA if the MapCode is already is use.", "The species name ");
                    mapCodeList.Add(mapCode, listOfCohorts);
                }
                else
                {
                    int age = System.Convert.ToInt32(row["CohortAge"]);
                    int biomass = System.Convert.ToInt32(row["CohortBiomass"]);

                    ISpecies species = speciesDataset[speciesName];
                    if (species == null)
                        throw new InputValueException(speciesName, "{0} is not a species name.", speciesName);
                    if (age == 0)
                        throw new InputValueException(age.ToString(), "Ages must be > 0.");
                    if (age > species.Longevity)
                    {
                        Console.WriteLine("WARNING!  AGE OF AN INITIAL CONDITION COHORT ({species, age}) EXCEEDS LONGEVITY.  AGE ASSIGNED=LONGEVITY-TIMESTEP TO ALLOW COHORT TO REPRODUCE PRIOR TO DEATH.  !!WARNING!!");
                        age = species.Longevity - successionTimestep;
                    }

                    IDictionary<string, object> tempObject = addParams;
                    

                    foreach (var test in this.additionalParameters)
                    {
                        if (row.Table.Columns.Contains(test.Key))
                        {
                            tempObject.Add(test.Key, row[test.Key]);
                        }
                        else
                        {
                            if (!trackWarnings[test.Key])
                            {
                                Console.WriteLine("WARNING: " + test.Key + " not initialized in initial community file.");
                                trackWarnings[test.Key] = true;
                            }
                            tempObject.Add(test.Key, test.Value);
                        }
                    }

                    if (!mapCodeList.ContainsKey(mapCode))
                    {
                        mapCodeList.Add(mapCode, listOfCohorts);
                        mapCodeList[mapCode].Add(new SpeciesCohorts(species, (ushort)age, biomass, addParams));
                    }
                    else
                    {
                        mapCodeList[mapCode].Add(new SpeciesCohorts(species, (ushort)age, biomass, addParams));
                    }

                }

            }

            foreach (KeyValuePair<int, List<ISpeciesCohorts>> kvp in mapCodeList)
            {
                dataset.Add(new Community((uint) kvp.Key, kvp.Value));
            }

            return dataset;

        }

        //---------------------------------------------------------------------

        private Dictionary<ushort, uint> BinAges(Dictionary<ushort, uint> ageBios)
        {
            if (successionTimestep <= 0)
                return ageBios;

            Dictionary<ushort, uint> newList = new Dictionary<ushort, uint>();

            //ageBios.Sort();
            //for (int i = 0; i < ages.Count; i++) {
            //    ushort age = ages[i];
            //    if (age % successionTimestep != 0)
            //        ages[i] = (ushort) (((age / successionTimestep) + 1) * successionTimestep);
            //}

            foreach(ushort age in ageBios.Keys)
            {
                if (age % successionTimestep == 0)
                {
                    if (newList.ContainsKey(age))
                    {
                        newList[age] += ageBios[age];
                    }
                    else
                    {
                        newList.Add(age, ageBios[age]);
                    }
                }
                else
                {
                    ushort new_age = (ushort)(((age / successionTimestep) + 1) * successionTimestep);
                    if (newList.ContainsKey(new_age))
                        newList[new_age] += ageBios[age];
                    else
                        newList.Add(new_age, ageBios[age]);
                }
            }

            //    Remove duplicates, by going backwards through list from last
            //    item to the 2nd item, comparing each item with the one before
            //    it.
            //for (int i = ages.Count - 1; i >= 1; i--) {
            //    if (ages[i] == ages[i-1])
            //        ages.RemoveAt(i);
            //}

            return newList; 
        }

        public static InputValue<uint> ReadBiomass(StringReader reader)
        {
            TextReader.SkipWhitespace(reader);
            //index = reader.Index;

            //  Read left parenthesis
            int nextChar = reader.Peek();
            if (nextChar == -1)
                throw new InputValueException();  // Missing value
            if (nextChar != '(')
                throw MakeInputValueException(TextReader.ReadWord(reader),
                                              "Value does not start with \"(\"");
            
            StringBuilder valueAsStr = new StringBuilder();
            valueAsStr.Append((char)(reader.Read()));

            //  Read whitespace between '(' and percentage
            valueAsStr.Append(ReadWhitespace(reader));

            //  Read percentage
            string word = ReadWord(reader, ')');
            if (word == "")
                throw MakeInputValueException(valueAsStr.ToString(),
                                              "No biomass after \"(\"");
            valueAsStr.Append(word);
            uint biomass;
            try
            {
                biomass = (uint) Int32.Parse(word); 
            }
            catch (System.FormatException exc)
            {
                throw MakeInputValueException(valueAsStr.ToString(),
                                              exc.Message);
            }

            if (biomass < 0.0 || biomass > 500000)
                throw MakeInputValueException(valueAsStr.ToString(),
                                              string.Format("{0} is not between 0 and 500,000", biomass));

            //  Read whitespace and ')'
            valueAsStr.Append(ReadWhitespace(reader));
            char? ch = TextReader.ReadChar(reader);
            if (!ch.HasValue)
                throw MakeInputValueException(valueAsStr.ToString(),
                                              "Missing \")\"");
            valueAsStr.Append(ch.Value);
            if (ch != ')')
                throw MakeInputValueException(valueAsStr.ToString(),
                                              string.Format("Value ends with \"{0}\" instead of \")\"", ch));
            
            //Landis.Library.Succession.Model.Core.UI.WriteLine("Read in biomass value: {0}", biomass);

            return new InputValue<uint>(biomass, "Biomass gm-2"); 
        }
        //---------------------------------------------------------------------

        /// <summary>
        /// Reads whitespace from a string reader.
        /// </summary>
        public static string ReadWhitespace(StringReader reader)
        {
            StringBuilder whitespace = new StringBuilder();
            int i = reader.Peek();
            while (i != -1 && char.IsWhiteSpace((char)i))
            {
                whitespace.Append((char)reader.Read());
                i = reader.Peek();
            }
            return whitespace.ToString();
        }
        //---------------------------------------------------------------------

        /// <summary>
        /// Reads a word from a string reader.
        /// </summary>
        /// <remarks>
        /// The word is terminated by whitespace, the end of input, or a
        /// particular delimiter character.
        /// </remarks>
        public static string ReadWord(StringReader reader,
                                      char delimiter)
        {
            StringBuilder word = new StringBuilder();
            int i = reader.Peek();
            while (i != -1 && !char.IsWhiteSpace((char)i) && i != delimiter)
            {
                word.Append((char)reader.Read());
                i = reader.Peek();
            }
            return word.ToString();
        }
    }
}

