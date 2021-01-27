import { getInput, getVariable } from "azure-pipelines-task-lib";
import { IDependabotUpdate } from "../models/IDependabotUpdate";

/**
 * Get dependency update configuration from inputs provided in the task
 *
 * To view YAML file format, visit
 * https://docs.github.com/en/github/administering-a-repository/configuration-options-for-dependency-updates#allow
 *
 * @returns {IDependabotUpdate[]} updates - array of dependency update configurations
 */
export default function getDependabotConfigFromInputs() {
  var dependabotUpdate: IDependabotUpdate = {
    packageEcosystem: getInput("packageManager", true),
    directory: getInput("directory", false),

    openPullRequestLimit: parseInt(getInput("openPullRequestsLimit", true)),
    devopsExtraCredentials: getVariable("DEPENDABOT_EXTRA_CREDENTIALS"),

    allow: getVariable("DEPENDABOT_ALLOW"),
    ignore: getVariable("DEPENDABOT_IGNORE"),

    targetBranch: getInput("targetBranch", false),
    versioningStrategy: getInput("versioningStrategy", true),
  };

  return [dependabotUpdate];
}
