import {
  getVariable,
  getBoolInput,
  getInput,
  getDelimitedInput,
} from "azure-pipelines-task-lib/task";
import extractHostname from "./extractHostname";
import extractOrganization from "./extractOrganization";
import extractVirtualDirectory from "./extractVirtualDirectory";
import getAzureDevOpsAccessToken from "./getAzureDevOpsAccessToken";
import getDockerImageTag from "./getDockerImageTag";
import getGithubAccessToken from "./getGithubAccessToken";
import getTargetRepository from "./getTargetRepository";

interface ISharedVariables {
  /** Organization URL protocol */
  protocol: string;
  /** Organization URL hostname */
  hostname: string;
  /** Organization URL hostname */
  port: string;
  /** Organization URL virtual directory */
  virtualDirectory: string;
  /** Organization name */
  organization: string;
  project: string;
  /** Determines if the pull requests that dependabot creates should have auto complete set */
  setAutoComplete: boolean;
  /** List of any policy configuration Id's which auto-complete should not wait for */
  autoCompleteIgnoreConfigIds: number[];
  /** Determines if the execution should fail when an exception occurs */
  failOnException: boolean;
  excludeRequirementsToUnlock: string;
  updaterOptions: string;
  /** Determines if the pull requests that dependabot creates should be automatically approved */
  autoApprove: boolean;
  /** The email of the user that should approve the PR */
  autoApproveUserEmail: string;
  /** A personal access token of the user that should approve the PR */
  autoApproveUserToken: string;
  extraCredentials: string;
  securityAdvisoriesEnabled: boolean;
  securityAdvisoriesFile: string | undefined;
  securityAdvisoriesJson: string | undefined;
  /** Registry of the docker image to be pulled */
  dockerImageRegistry: string | undefined;
  /** Repository of the docker image to be pulled */
  dockerImageRepository: string;
  /** Tag of the docker image to be pulled */
  dockerImageTag: string;
  /** the github token */
  githubAccessToken: string;
  /** the access User for Azure DevOps Repos */
  systemAccessUser: string;
  /** the access token for Azure DevOps Repos */
  systemAccessToken: string;
  /** Dependabot's target repository */
  repository: string;
  /** override value for allow */
  allowOvr: string;
  /** override value for ignore */
  ignoreOvr: string;
  /** Flag used to check if to use dependabot.yml or task inputs */
  useConfigFile: boolean;
  /** Flag used to forward the host ssh socket */
  forwardHostSshSocket: boolean;
  /** List of extra environment variables */
  extraEnvironmentVariables: string[];
  /** Merge strategies which can be used to complete a pull request */
  mergeStrategy: string;
  /** Determines whether to skip creating/updating pull requests */
  skipPullRequests: boolean;
}

/**
 * Extract shared variables
 *
 * @returns shared variables
 */
export default function getSharedVariables(): ISharedVariables {
  // Prepare shared variables
  let organizationUrl = getVariable("System.TeamFoundationCollectionUri");
  let parsedUrl = new URL(organizationUrl);
  let protocol: string = parsedUrl.protocol.slice(0, -1);
  let hostname: string = extractHostname(parsedUrl);
  let port: string = parsedUrl.port;
  let virtualDirectory: string = extractVirtualDirectory(parsedUrl);
  let organization: string = extractOrganization(organizationUrl);
  let project: string = encodeURI(getVariable("System.TeamProject")); // encode special characters like spaces
  let setAutoComplete = getBoolInput("setAutoComplete", false);
  let autoCompleteIgnoreConfigIds = getDelimitedInput(
    "autoCompleteIgnoreConfigIds",
    ";",
    false
  ).map(Number);
  let failOnException = getBoolInput("failOnException", true);
  let excludeRequirementsToUnlock = getInput("excludeRequirementsToUnlock") || "";
  let updaterOptions = getInput("updaterOptions");
  let autoApprove: boolean = getBoolInput("autoApprove", false);
  let autoApproveUserEmail: string = getInput("autoApproveUserEmail");
  let autoApproveUserToken: string = getInput("autoApproveUserToken");
  let extraCredentials = getVariable("DEPENDABOT_EXTRA_CREDENTIALS");
  let securityAdvisoriesEnabled = getBoolInput("securityAdvisories", false);
  let securityAdvisoriesFile: string | undefined = getInput('securityAdvisoriesFile');
  let securityAdvisoriesJson = getVariable("DEPENDABOT_SECURITY_ADVISORIES_JSON");
  let dockerImageRegistry: string | undefined = getInput('dockerImageRegistry');
  let dockerImageRepository: string = getInput('dockerImageRepository', true);
  let dockerImageTag: string = getDockerImageTag();

  // Prepare the github token, if one is provided
  let githubAccessToken: string = getGithubAccessToken();

  // Prepare the Azure Devops User, if one is provided
  let systemAccessUser: string = getInput("azureDevOpsUser");

  // Prepare the access token for Azure DevOps Repos.
  let systemAccessToken: string = getAzureDevOpsAccessToken();

  // Prepare the repository
  let repository: string = getTargetRepository();

  // Get the override values for allow, ignore, and labels
  let allowOvr = getVariable("DEPENDABOT_ALLOW_CONDITIONS");
  let ignoreOvr = getVariable("DEPENDABOT_IGNORE_CONDITIONS");

  // Check if to use dependabot.yml or task inputs
  let useConfigFile: boolean = getBoolInput("useConfigFile", false);

  // Check if the host ssh socket needs to be forwarded to the container
  let forwardHostSshSocket: boolean = getBoolInput("forwardHostSshSocket", false);

  // prepare extra env variables
  let extraEnvironmentVariables = getDelimitedInput(
    "extraEnvironmentVariables",
    ";",
    false
  );

  // Get the selected merge strategy
  let mergeStrategy = getInput("mergeStrategy", true);

  // Check if to skip creating/updating pull requests
  let skipPullRequests: boolean = getBoolInput("skipPullRequests", false);

  return {
    protocol,
    hostname,
    port,
    virtualDirectory,
    organization,
    project,
    setAutoComplete,
    autoCompleteIgnoreConfigIds,
    failOnException,
    excludeRequirementsToUnlock,
    updaterOptions: updaterOptions,
    autoApprove,
    autoApproveUserEmail,
    autoApproveUserToken,
    extraCredentials,
    securityAdvisoriesEnabled,
    securityAdvisoriesFile,
    securityAdvisoriesJson,
    dockerImageRegistry,
    dockerImageRepository,
    dockerImageTag,
    githubAccessToken,
    systemAccessUser,
    systemAccessToken,
    repository,
    allowOvr,
    ignoreOvr,
    useConfigFile,
    forwardHostSshSocket,
    extraEnvironmentVariables,
    mergeStrategy,
    skipPullRequests,
  };
}
