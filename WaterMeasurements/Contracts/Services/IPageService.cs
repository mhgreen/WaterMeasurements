﻿using System;

namespace WaterMeasurements.Contracts.Services;

public interface IPageService
{
    Type GetPageType(string key);
}
