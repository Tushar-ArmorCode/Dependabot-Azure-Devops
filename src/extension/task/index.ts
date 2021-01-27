import tl = require("azure-pipelines-task-lib/task");
import tr = require("azure-pipelines-task-lib/toolrunner");
import { IDependabotUpdate } from "./models/IDependabotUpdate";
import getDependabotConfigFromInputs from "./utils/getDependabotConfigFromInputs";
import parseDependabotConfigFile from "./utils/parseDependabotConfigFile";

function getGithubEndPointToken(githubEndpoint: string): string {
  const githubEndpointObject = tl.getEndpointAuthorization(
    githubEndpoint,
    false
  );
  let githubEndpointToken: string = null;

  if (!!githubEndpointObject) {
    tl.debug("Endpoint scheme: " + githubEndpointObject.scheme);

    if (githubEndpointObject.scheme === "PersonalAccessToken") {
      githubEndpointToken = githubEndpointObject.parameters.accessToken;
    } else if (githubEndpointObject.scheme === "OAuth") {
      githubEndpointToken = githubEndpointObject.parameters.AccessToken;
    } else if (githubEndpointObject.scheme === "Token") {
      githubEndpointToken = githubEndpointObject.parameters.AccessToken;
    } else if (githubEndpointObject.scheme) {
      throw new Error(
        tl.loc("InvalidEndpointAuthScheme", githubEndpointObject.scheme)
      );
    }
  }

  if (!githubEndpointToken) {
    throw new Error(tl.loc("InvalidGitHubEndpoint", githubEndpoint));
  }

  return githubEndpointToken;
}

function extractOrganization(organizationUrl: string): string {
  let parts = organizationUrl.split("/");

  // Check for new style: https://dev.azure.com/x/
  if (parts.length === 5) {
    return parts[3];
  }

  // Check for old style: https://x.visualstudio.com/
  if (parts.length === 4) {
    // Get x.visualstudio.com part.
    let part = parts[2];

    // Return organization part (x).
    return part.split(".")[0];
  }

  tl.setResult(
    tl.TaskResult.Failed,
    `Error parsing organization from organization url: '${organizationUrl}'.`
  );
}

function extractHostname(organizationUrl: string): string {
  let parts = organizationUrl.split("/");

  // For both new (https://dev.azure.com/x/) and old style (https://x.visualstudio.com/), the hostname is in position 2
  return parts[2];
}

async function run() {
  try {
    // Checking if docker is installed
    tl.debug("Checking for docker install ...");
    tl.which("docker", true);

    // Prepare the docker task
    let dockerRunner: tr.ToolRunner = tl.tool(tl.which("docker", true));
    dockerRunner.arg(["run"]); // run command
    dockerRunner.arg(["--rm"]); // remove after execution
    dockerRunner.arg(["-i"]); // attach pseudo tty

    // Set the hostname
    var organizationUrl = tl.getVariable("System.TeamFoundationCollectionUri");
    let hostname: string = extractHostname(organizationUrl);
    dockerRunner.arg(["-e", `AZURE_HOSTNAME=${hostname}`]);

    // Set the github token, if one is provided
    const githubEndpointId = tl.getInput("gitHubConnection");
    if (githubEndpointId) {
      tl.debug(
        "GitHub connection supplied. A token shall be extracted from it."
      );
      let githubAccessToken: string = getGithubEndPointToken(githubEndpointId);
      dockerRunner.arg(["-e", `GITHUB_ACCESS_TOKEN=${githubAccessToken}`]);
    }

    // Set the access token for Azure DevOps Repos.
    // If the user has not provided one, we use the one from the SystemVssConnection
    let systemAccessToken: string = tl.getInput("azureDevOpsAccessToken");
    if (!systemAccessToken) {
      tl.debug(
        "No custom token provided. The SystemVssConnection's AccessToken shall be used."
      );
      systemAccessToken = tl.getEndpointAuthorizationParameter(
        "SystemVssConnection",
        "AccessToken",
        false
      );
    }
    dockerRunner.arg(["-e", `AZURE_ACCESS_TOKEN=${systemAccessToken}`]);

    // Set the organization
    let organization: string = extractOrganization(organizationUrl);
    dockerRunner.arg(["-e", `AZURE_ORGANIZATION=${organization}`]);

    // Set the project
    let project: string = tl.getVariable("System.TeamProject");
    project = encodeURI(project); // encode special characters like spaces
    dockerRunner.arg(["-e", `AZURE_PROJECT=${project}`]);

    // Set the repository
    let repository: string = tl.getVariable("Build.Repository.Name");
    repository = encodeURI(repository); // encode special characters like spaces
    dockerRunner.arg(["-e", `AZURE_REPOSITORY=${repository}`]);

    //check if user has requested the use of dependabot YAML config file for updates
    let useConfigFile: boolean = tl.getBoolInput("useConfigFile", false);
    var updates: IDependabotUpdate[];

    if (useConfigFile) updates = parseDependabotConfigFile();
    else updates = getDependabotConfigFromInputs();

    for (const update of updates) {
      dockerRunner.arg([
        "-e",
        `DEPENDABOT_PACKAGE_MANAGER=${update.packageEcosystem}`,
      ]);

      // Set the directory
      if (update.directory) {
        dockerRunner.arg(["-e", `DEPENDABOT_DIRECTORY=${update.directory}`]);
      }

      // Set the target branch
      if (update.targetBranch) {
        dockerRunner.arg([
          "-e",
          `DEPENDABOT_TARGET_BRANCH=${update.targetBranch}`,
        ]);
      }

      // Set the versioning strategy
      if (update.versioningStrategy) {
        dockerRunner.arg([
          "-e",
          `DEPENDABOT_VERSIONING_STRATEGY=${update.versioningStrategy}`,
        ]);
      }
      // Set the open pull requests limit
      if (update.openPullRequestLimit) {
        dockerRunner.arg([
          "-e",
          `DEPENDABOT_OPEN_PULL_REQUESTS_LIMIT=${update.openPullRequestLimit}`,
        ]);
      }

      // Set the extra credentials
      if (update.devopsExtraCredentials) {
        dockerRunner.arg([
          "-e",
          `DEPENDABOT_EXTRA_CREDENTIALS=${update.devopsExtraCredentials}`,
        ]);
      }

      // Set the dependencies to allow
      if (update.allow) {
        dockerRunner.arg(["-e", `DEPENDABOT_ALLOW=${update.allow}`]);
      }

      // Set the dependencies to ignore
      if (update.ignore) {
        dockerRunner.arg(["-e", `DEPENDABOT_IGNORE=${update.ignore}`]);
      }

      // Allow overriding of the docker image tag globally
      let dockerImageTag: string = tl.getVariable(
        "DEPENDABOT_DOCKER_IMAGE_TAG"
      );
      if (!dockerImageTag) {
        dockerImageTag = "0.2"; // will pull the latest patch for 0.2 e.g. 0.2.0
      }

      const dockerImage = `tingle/dependabot-azure-devops:${dockerImageTag}`;
      tl.debug(`Running docker container using '${dockerImage}' ...`);
      dockerRunner.arg([dockerImage]);

      // Now execute using docker
      await dockerRunner.exec();
    }

    tl.debug("Docker container execution completed!");
  } catch (err) {
    tl.setResult(tl.TaskResult.Failed, err.message);
  }
}

run();
