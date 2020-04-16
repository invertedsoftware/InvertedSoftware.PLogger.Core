/* Copyright (c) Year(2020), Inverted Software
 *
 * Permission to use, copy, modify, and/or distribute this software for any purpose with or without fee is hereby granted, 
 * provided that the above copyright notice and this permission notice appear in all copies.
 *
 * THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS.
 * IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES WHATSOEVER RESULTING FROM LOSS OF USE,
 * DATA OR PROFITS, WHETHER IN AN ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
 */

using System;

using System.Data;
using Microsoft.Data.SqlClient;

namespace InvertedSoftware.PLogger.Core
{
    /// <summary>
    /// This is a Sql server implementation of a log provider
    /// </summary>
    public sealed class SqlLogProvider : BaseLogProvider
    {
        #region Private Members
        /// <summary>
        /// The connection to use when logging messages
        /// </summary>
        private SqlConnection connection = new SqlConnection();

        /// <summary>
        /// The commad to use when logging messages
        /// </summary>
        private SqlCommand command = new SqlCommand();
        #endregion

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="pConfig">The configuration section</param>
        public SqlLogProvider(PLoggerSettings pConfig)
            : base(pConfig)
        {
            // Set the configuraton values
            InitParameters();
        }

        /// <summary>
        /// Initialize the Sql paramters
        /// </summary>
        private void InitParameters()
        {
            connection.ConnectionString = pConfig.StringConnection;
            command.Connection = connection;
            command.CommandType = CommandType.StoredProcedure;
            command.CommandText = pConfig.StoredProcedureName;
            command.Parameters.Add(new SqlParameter() { ParameterName = pConfig.MessageParameterName });
        }

        /// <summary>
        /// Process a single log entry
        /// </summary>
        /// <param name="data">The string to write to a file</param>
        protected override void WriteLogItem(string data)
        {
            try
            {
                if (connection.State != ConnectionState.Open)
                    connection.Open();
                command.Parameters[0].Value = data;
                command.ExecuteNonQuery();
                connection.Close();
            }
            catch (Exception ex)
            {
                throw new Exception("Cannot write log item to database.", ex);
            }
        }
    }
}