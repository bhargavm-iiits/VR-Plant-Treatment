if (BTP.Core.EnvironmentController.Instance == null) throw new System.Exception("Bootstrap has not initialized.");
BTP.Core.EnvironmentController.Instance.EnterLab();
return "Requested Lab from fresh bootstrap";
