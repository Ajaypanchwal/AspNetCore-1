﻿using IntegrationTestMVC;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace IntegrationTest
{
    public class TestStartup : Startup
    {
        public TestStartup(IConfiguration configuration):base(configuration)
        {

        }
        

    }
}
